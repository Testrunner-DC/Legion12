using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class YomiRecoverLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "黄泉回收生命周期",
            MasterId = "S01-04D1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "yomi-recover", "YOMI-RECOVER", seed,
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

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "S01-04D1"));

    [Fact]
    public void PrintedThirdAbilityUsesOneStructuredGraveRecoveryScene()
    {
        var ability = Catalog.AtomicEffects.Find("S01-04D1")!.Abilities
            .Single(candidate => candidate.Sequence == 3);
        var scene = Assert.Single(ability.Presentations, candidate =>
            candidate.Flow == "yomi-grave-recover");
        Assert.Equal((1, 1), (scene.SegmentIndex, scene.SegmentCount));
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiRecover")]
    public void ConvertedDerivedSpecialCardCannotBeOfferedAsAHandRecoveryTarget()
    {
        var game = Create(91421);
        var player = game.State.Players[0];
        player.ExtraRelics.Add(Card("S02-0008", "yomi-special-ring"));
        player.Graveyard.Add(Card("S02-01S1", "yomi-special-xiaotian"));

        var result = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover"));

        Assert.False(result.Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(player.MasterTapped);
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiRecover")]
    public void TargetLostDuringResponsePublishesFailedAndKeepsTheActiveRestCost()
    {
        var game = Create(91422);
        var player = game.State.Players[0];
        var target = Card("S01-0401", "yomi-lost-target");
        player.Graveyard.Add(target);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        player.Graveyard.Remove(target);
        player.Removed.Add(target);
        PassResponses(game);

        Assert.True(player.MasterTapped);
        Assert.Contains(target, player.Removed);
        Assert.Empty(player.Hand);
        Assert.Equal("failed", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiRecover")]
    public void NegationKeepsTheActiveRestCostAndDoesNotMoveTheGraveCard()
    {
        var game = Create(91423);
        var player = game.State.Players[0];
        var target = Card("S01-0401", "yomi-negated-target");
        player.Graveyard.Add(target);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(player.MasterTapped);
        Assert.Contains(target, player.Graveyard);
        Assert.Empty(player.Hand);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiRecover")]
    public void V2RestoreMovesTheFrozenTargetOnceAndRejectsTheOldPrompt()
    {
        var game = Create(91424);
        var player = game.State.Players[0];
        var target = Card("S01-0401", "yomi-restore-target");
        player.Graveyard.Add(target);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover")).Accepted);
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
        Assert.True(restored.MasterTapped);
        Assert.Contains(restored.Hand, card => card.InstanceId == target.InstanceId);
        Assert.DoesNotContain(restored.Graveyard, card => card.InstanceId == target.InstanceId);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
    }
}
