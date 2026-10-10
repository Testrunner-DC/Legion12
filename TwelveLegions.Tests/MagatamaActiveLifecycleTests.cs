using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MagatamaActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "magatama-active", "MAGATAMA-ACTIVE", seed,
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
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    private static (L12CardInstance Source, L12CardInstance Legion) ArrangeMove(L12GameEngine game,
        string suffix)
    {
        var player = game.State.Players[0];
        var source = Card("S02-0404", $"magatama-{suffix}-source");
        var legion = Card("S01-0409", $"magatama-{suffix}-legion");
        player.Relic = source;
        player.Field[0][0] = legion;
        return (source, legion);
    }

    private static void CommitMove(L12GameEngine game, L12CardInstance source,
        L12CardInstance legion, string destination = "1:2")
    {
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "magatamaMove")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: legion.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: destination)).Accepted);
    }

    [Fact]
    public void BothRuntimeModesSharePrintedAbilityTwoButUseDistinctStructuredScenes()
    {
        var ability = Catalog.AtomicEffects.Find("S02-0404")!.Abilities
            .Single(candidate => candidate.Sequence == 2);

        Assert.Contains(ability.Presentations, scene => scene.Flow == "magatama-cavalry-move"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
        Assert.Contains(ability.Presentations, scene => scene.Flow == "magatama-immortal"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaMove")]
    public void MoveModeRejectsBeforePromptWhenNoFriendlyReadyLegionCanMove()
    {
        var game = Create(91431);
        var source = Card("S02-0404", "magatama-empty-source");
        game.State.Players[0].Relic = source;

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "magatamaMove"));

        Assert.False(result.Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(source.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaMove")]
    public void MoveModeCanCancelBeforeRestingTheArtifact()
    {
        var game = Create(91432);
        var (source, legion) = ArrangeMove(game, "cancel");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "magatamaMove")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.False(source.Tapped);
        Assert.Same(legion, game.State.Players[0].Field[0][0]);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaMove")]
    public void MoveTargetBecomingRestedDuringResponseFailsAndKeepsTheRestCost()
    {
        var game = Create(91433);
        var (source, legion) = ArrangeMove(game, "target-failed");
        CommitMove(game, source, legion);
        legion.Tapped = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.True(legion.Tapped);
        Assert.Same(legion, game.State.Players[0].Field[0][0]);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaMove")]
    public void MoveDestinationBecomingOccupiedDuringResponseFailsWithoutRetargeting()
    {
        var game = Create(91434);
        var (source, legion) = ArrangeMove(game, "slot-failed");
        CommitMove(game, source, legion);
        var blocker = Card("S02-0401", "magatama-slot-blocker");
        game.State.Players[0].Field[1][2] = blocker;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Same(legion, game.State.Players[0].Field[0][0]);
        Assert.Same(blocker, game.State.Players[0].Field[1][2]);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaImmortal")]
    public void ImmortalTargetLosingItsMovementHistoryDuringResponseFailsAndKeepsCost()
    {
        var game = Create(91435);
        var player = game.State.Players[0];
        var source = Card("S02-0404", "magatama-immortal-failed-source");
        var legion = Card("S02-0401", "magatama-immortal-failed-legion");
        legion.LastMovedTurn = game.State.TurnSerial;
        player.Relic = source;
        player.Field[0][0] = legion;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "magatamaImmortal")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: legion.InstanceId)).Accepted);
        legion.LastMovedTurn = -1;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal(0, legion.ImmortalUses);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaImmortal")]
    public void NegationKeepsTheArtifactRestedAndDoesNotGrantImmortality()
    {
        var game = Create(91436);
        var player = game.State.Players[0];
        var source = Card("S02-0404", "magatama-negated-source");
        var legion = Card("S02-0401", "magatama-negated-legion");
        legion.LastMovedTurn = game.State.TurnSerial;
        player.Relic = source;
        player.Field[0][0] = legion;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "magatamaImmortal")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: legion.InstanceId)).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal(0, legion.ImmortalUses);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:magatamaMove")]
    public void V2RestoreMovesTheFrozenTargetOnceAndRejectsTheOldSlotPrompt()
    {
        var game = Create(91437);
        var (source, legion) = ArrangeMove(game, "restore");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "magatamaMove")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: legion.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "1:2")).Accepted);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "1:2")).Accepted);
        var restored = game.State.Players[0];
        Assert.Null(restored.Field[0][0]);
        Assert.Equal(legion.InstanceId, restored.Field[1][2]?.InstanceId);
        Assert.True(restored.Relic!.Tapped);
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
        Assert.Single(game.State.Events, entry => entry.Type == "move"
            && entry.Text.Contains("八尺琼勾玉", StringComparison.Ordinal));
    }
}
