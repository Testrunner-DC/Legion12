using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SingleActiveTargetStatePresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "single-active-target-state", "SINGLE-ACTIVE-TARGET-STATE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);

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

    private static void PrepareMain(L12GameEngine game)
    {
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0019", $"target-state-response-{game.State.StackSequence}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] ??= Card("S01-0004", $"target-state-response-target-{game.State.StackSequence}");
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

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    private static (L12CardInstance Source, L12CardInstance Discard) PrepareScarab(L12GameEngine game)
    {
        var player = game.State.Players[0];
        var source = Card("S02-0205", $"scarab-target-state-{game.State.StackSequence}");
        var discard = Card("S02-0003", $"scarab-target-state-cost-{game.State.StackSequence}");
        player.Relic = source;
        player.Hand.Clear();
        player.Hand.Add(discard);
        PrepareMain(game);
        HoldOpponentResponseWindow(game);
        return (source, discard);
    }

    private static void DeclareScarab(L12GameEngine game, L12CardInstance source,
        L12CardInstance discard, params string[] targetIds)
    {
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "scarabDebuff")).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            CardInstanceIds: targetIds.ToList())).Accepted);
    }

    private static (L12CardInstance Source, string TargetKey) PrepareShennong(L12GameEngine game,
        bool withTarget = true)
    {
        var player = game.State.Players[0];
        var source = Card("S02-0104", $"shennong-target-state-{game.State.StackSequence}");
        player.Relic = source;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = $"shennong-return-{game.State.StackSequence}",
            CardId = "S01-01C1",
        });
        const string targetKey = "active:master-0:drawCycle";
        if (withTarget) player.UsedAbilities.Add(targetKey);
        PrepareMain(game);
        HoldOpponentResponseWindow(game);
        return (source, targetKey);
    }

    [Fact]
    [Trait("L12Evidence", "ability:scarabDebuff")]
    public void ScarabWithNoTargetPaysItsCostAndPublishesSkipped()
    {
        var game = Create(91311);
        var (source, discard) = PrepareScarab(game);

        DeclareScarab(game, source, discard);
        PassResponses(game);

        Assert.Contains(discard, game.State.Players[0].Graveyard);
        Assert.Equal("skipped", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:scarabDebuff")]
    public void ScarabFailsWhenEveryDeclaredTargetLeavesBeforeSettlement()
    {
        var game = Create(91312);
        var enemy = game.State.Players[1];
        var (source, discard) = PrepareScarab(game);
        var target = Card("S01-0101", "scarab-all-invalid-target");
        enemy.Field[0][0] = target;

        DeclareScarab(game, source, discard, target.InstanceId);
        enemy.Field[0][0] = null;
        enemy.Graveyard.Add(target);
        PassResponses(game);

        Assert.Contains(discard, game.State.Players[0].Graveyard);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:scarabDebuff")]
    public void ScarabKeepsValidTargetsWhenOnlyOneOfTwoDeclarationsBecomesInvalid()
    {
        var game = Create(91313);
        var enemy = game.State.Players[1];
        var (source, discard) = PrepareScarab(game);
        var invalid = Card("S01-0101", "scarab-partial-invalid");
        var valid = Card("S01-0102", "scarab-partial-valid");
        enemy.Field[0][0] = invalid;
        enemy.Field[0][1] = valid;
        var troopsBefore = valid.CurrentTroops;

        DeclareScarab(game, source, discard, invalid.InstanceId, valid.InstanceId);
        enemy.Field[0][0] = null;
        enemy.Graveyard.Add(invalid);
        PassResponses(game);

        Assert.Equal(troopsBefore - 1000, valid.CurrentTroops);
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1个已声明对象", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:scarabDebuff")]
    public void NegatedScarabKeepsDiscardCostAndChangesNoTargets()
    {
        var game = Create(91314);
        var enemy = game.State.Players[1];
        var (source, discard) = PrepareScarab(game);
        var target = Card("S01-0101", "scarab-negated-target");
        enemy.Field[0][0] = target;
        var troopsBefore = target.CurrentTroops;

        DeclareScarab(game, source, discard, target.InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Contains(discard, game.State.Players[0].Graveyard);
        Assert.Equal(troopsBefore, target.CurrentTroops);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:shennongReset")]
    public void ShennongResetSurvivesCheckpointAndRejectsTheOldSelectionPrompt()
    {
        var game = Create(91315);
        var (source, targetKey) = PrepareShennong(game);
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "shennongReset")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: "drawCycle")).Accepted);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: "drawCycle")).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(targetKey, game.State.Players[0].UsedAbilities);
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:shennongReset")]
    public void ShennongRejectsWithoutAUsedMasterAbilityBeforeAnyPayment()
    {
        var game = Create(91316);
        var player = game.State.Players[0];
        var (source, _) = PrepareShennong(game, withTarget: false);

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "shennongReset"));

        Assert.False(result.Accepted);
        Assert.False(source.Tapped);
        Assert.Single(player.Morale);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:shennongReset")]
    public void ShennongFailsIfTheDeclaredUsageWasResetByAnEarlierResponse()
    {
        var game = Create(91317);
        var player = game.State.Players[0];
        var (source, targetKey) = PrepareShennong(game);
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "shennongReset")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: "drawCycle")).Accepted);
        player.UsedAbilities.Remove(targetKey);

        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:shennongReset")]
    public void NegatedShennongKeepsItsReturnAndRestCostsWithoutResettingUsage()
    {
        var game = Create(91318);
        var player = game.State.Players[0];
        var (source, targetKey) = PrepareShennong(game);
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "shennongReset")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: "drawCycle")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Contains(targetKey, player.UsedAbilities);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }
}
