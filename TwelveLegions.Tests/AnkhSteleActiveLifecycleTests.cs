using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AnkhSteleActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "ankh-active", "ANKH-ACTIVE", seed,
            ["甲", "乙"], [3, 3], skipPreparation: true,
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
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    [Fact]
    public void BothRuntimeModesShareThePrintedActiveAbilityButUseDistinctStructuredScenes()
    {
        var ability = Catalog.AtomicEffects.Find("S01-0215")!.Abilities.Single(candidate => candidate.Sequence == 2);

        Assert.Contains(ability.Presentations, scene => scene.Flow == "ankh-ready-guard"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
        Assert.Contains(ability.Presentations, scene => scene.Flow == "ankh-draw"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:ankhReady")]
    public void ReadyModeRequiresItsDiscardCostBeforeOfferingAnyTargetPrompt()
    {
        var game = Create(91381);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "ankh-no-hand-source");
        var guard = Card("S01-0212", "ankh-no-hand-guard");
        guard.Tapped = true;
        player.Relic = source;
        player.Field[0][0] = guard;

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhReady"));

        Assert.False(result.Accepted);
        Assert.Contains("手牌", result.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(source.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "ability:ankhReady")]
    public void ReadyModeCanCancelBeforePaymentWithoutChangingState()
    {
        var game = Create(91382);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "ankh-cancel-source");
        var guard = Card("S01-0212", "ankh-cancel-guard");
        var discard = Card("S01-0001", "ankh-cancel-discard");
        guard.Tapped = true;
        player.Relic = source;
        player.Field[0][0] = guard;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhReady")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.False(source.Tapped);
        Assert.True(guard.Tapped);
        Assert.Contains(discard, player.Hand);
        Assert.Empty(player.Graveyard);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:ankhReady")]
    public void ReadyModeRevalidatesItsTargetAfterResponseAndKeepsPaidCostsOnFailure()
    {
        var game = Create(91383);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "ankh-failed-source");
        var guard = Card("S01-0212", "ankh-failed-guard");
        var discard = Card("S01-0001", "ankh-failed-discard");
        guard.Tapped = true;
        player.Relic = source;
        player.Field[0][0] = guard;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhReady")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: guard.InstanceId)).Accepted);
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            Choice: discard.InstanceId)).Accepted);
        guard.Tapped = false;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.False(guard.Tapped);
        Assert.Contains(discard, player.Graveyard);
        var result = Result(game, source.InstanceId);
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
    }

    [Fact]
    [Trait("L12Evidence", "ability:ankhReady")]
    public void ReadyModeRestoresAfterPaymentAndRejectsTheOldCostPromptTwice()
    {
        var game = Create(91386);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "ankh-restore-source");
        var guard = Card("S01-0212", "ankh-restore-guard");
        var discard = Card("S01-0001", "ankh-restore-discard");
        guard.Tapped = true;
        player.Relic = source;
        player.Field[0][0] = guard;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhReady")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: guard.InstanceId)).Accepted);
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            Choice: discard.InstanceId)).Accepted);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            Choice: discard.InstanceId)).Accepted);
        var restoredPlayer = game.State.Players[0];
        Assert.True(restoredPlayer.Relic!.Tapped);
        Assert.False(Assert.Single(restoredPlayer.Field[0], card => card?.InstanceId == guard.InstanceId)!.Tapped);
        Assert.Contains(restoredPlayer.Graveyard, card => card.InstanceId == discard.InstanceId);
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:ankhDraw")]
    public void DrawModeEmptyLibraryFailsAfterPayingBothRestCosts()
    {
        var game = Create(91384);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "ankh-empty-source");
        var guard = Card("S01-0212", "ankh-empty-guard");
        player.Relic = source;
        player.Field[0][0] = guard;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhDraw")).Accepted);
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            Choice: guard.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.True(guard.Tapped);
        Assert.Equal(1, game.State.Winner);
        var result = Result(game, source.InstanceId);
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
    }

    [Fact]
    [Trait("L12Evidence", "ability:ankhDraw")]
    public void NegatedDrawModeDoesNotDrawAndKeepsBothRestCosts()
    {
        var game = Create(91385);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "ankh-negated-source");
        var guard = Card("S01-0212", "ankh-negated-guard");
        var draw = Card("S01-0001", "ankh-negated-draw");
        player.Relic = source;
        player.Field[0][0] = guard;
        player.Library.Add(draw);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhDraw")).Accepted);
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            Choice: guard.InstanceId)).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.True(guard.Tapped);
        Assert.Single(player.Library);
        Assert.Empty(player.Hand);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }
}
