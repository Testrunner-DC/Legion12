using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch4RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMaster = null)
    {
        var first = Catalog.DeckAt(0);
        if (firstMaster is not null)
            first = new L12PresetDeckDefinition
            {
                Name = $"{firstMaster}第四批原子审查牌库",
                MasterId = firstMaster,
                CardIds = [.. first.CardIds],
                MoraleIds = [.. first.MoraleIds],
                SpecialIds = [],
            };
        var game = new L12GameEngine(Catalog, "atomic-review-batch4", "ATOMIC4", seed,
            ["甲", "乙"], [first, Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 50 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        return prompt;
    }

    [Fact]
    [Trait("L12Evidence", "trigger:tsukuyomiFollowMove")]
    public void TsukuyomiDeclaresCostTargetAndSlotBeforeStackAndDoesNotRefundOnTargetLoss()
    {
        var game = Create(7001, "S02-04M1");
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "atomic4-tsukuyomi-moved");
        var target = Card("S02-0402", "atomic4-tsukuyomi-target");
        player.Field[1][0] = moved;
        player.Field[0][2] = target;
        var movementMorale = new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "atomic4-tsukuyomi-move-cost", Tapped = false,
        };
        var morale = new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "atomic4-tsukuyomi-cost", Tapped = false,
        };
        player.Morale.AddRange([movementMorale, morale]);

        Assert.True(game.Handle(0, new L12Command("move", moved.InstanceId, Row: 0, Slot: 0)).Accepted);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", order.Continuation);
        var followMove = Assert.Single(order.ValidChoices,
            id => order.Data[id].Contains("军团位移时效果", StringComparison.Ordinal));
        var attackBuff = Assert.Single(order.ValidChoices, id => id != followMove);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [attackBuff, followMove])).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Single(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("ability") == "tsukuyomiFrontAttackBuff");
        Resolve(game, "mode:use");
        // 唯一合法的士气费用由公共费用组件自动选中，目标与位置仍由玩家选择。
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, targetPrompt.ValidChoices);
        Assert.False(morale.Tapped);
        Resolve(game, target.InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:1", slot.ValidChoices);
        Assert.False(morale.Tapped);
        Resolve(game, "0:1");

        Assert.True(morale.Tapped);
        Assert.Contains("active:master-0:tsukuyomiFollowMove", player.UsedAbilities);
        Assert.Equal(2, game.State.EffectStack.Count);
        player.Field[0][2] = null;
        player.Graveyard.Add(target);
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.Contains(target, player.Graveyard);
        Assert.Null(player.Field[0][1]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("不回滚", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0106")]
    public void CosmosYinKeepsTopCardHiddenUntilResolutionThenDeclaresPublicTarget()
    {
        var game = Create(7002);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "atomic4-cosmos-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        var target = Card("S02-0402", "atomic4-cosmos-target");
        owner.Field[0][0] = target;
        owner.Library.Clear();
        var hiddenTop = Card("S01-0109", "atomic4-cosmos-hidden-top");
        owner.Library.Add(hiddenTop);
        var baseTactic = Card("S01-0219", "atomic4-cosmos-base");
        actor.Hand.Add(baseTactic);
        for (var index = 0; index < baseTactic.CurrentCost; index++)
            actor.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-02C1", InstanceId = $"atomic4-cosmos-base-cost-{index}", Tapped = false,
            });
        game.State.ActivePlayer = 1;

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        var firstPriority = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, firstPriority.PlayerIndex);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: firstPriority.PromptId,
            Choice: "pass")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, response.PlayerIndex);
        Assert.Equal("response", response.Kind);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Assert.DoesNotContain(hiddenTop.InstanceId, response.ValidChoices);
        Assert.DoesNotContain(hiddenTop.InstanceId, response.Data.Values);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.ValidChoices.Contains(hiddenTop.InstanceId)
                || prompt.Data.Values.Contains(hiddenTop.InstanceId));
        PassResponses(game);

        Assert.True(game.State.PendingPrompts.Count == 1,
            string.Join(" | ", game.State.Events.TakeLast(12).Select(entry => $"{entry.Type}:{entry.Text}")));
        var delayed = game.State.PendingPrompts[0];
        Assert.Equal("post-hidden-reveal", delayed.Data["declarationTiming"]);
        Assert.Contains(target.InstanceId, delayed.ValidChoices);
        Assert.Contains(hiddenTop, owner.Graveyard);
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == baseTactic.InstanceId);
        Assert.DoesNotContain(actor.Graveyard, card => card.InstanceId == baseTactic.InstanceId);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == baseTactic.InstanceId));
        var delayedPromptId = delayed.PromptId;
        var targetId = target.InstanceId;
        var hiddenTopId = hiddenTop.InstanceId;

        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredDelayed = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(delayedPromptId, restoredDelayed.PromptId);
        Assert.Equal("post-hidden-reveal", restoredDelayed.Data["declarationTiming"]);
        var restoredOwner = game.State.Players[0];
        var restoredTarget = Assert.Single(restoredOwner.Field[0], card => card?.InstanceId == targetId)!;
        restoredOwner.Field[0][0] = null;
        restoredOwner.Graveyard.Add(restoredTarget);
        Resolve(game, targetId);
        var duplicate = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: delayedPromptId, Choice: targetId));

        Assert.False(duplicate.Accepted);
        Assert.Contains(restoredOwner.Graveyard, card => card.InstanceId == hiddenTopId);
        Assert.DoesNotContain(restoredOwner.Library, card => card.InstanceId == hiddenTopId);
        var failed = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId)
            && entry.EffectSegmentIndex == 2);
        Assert.Equal("failed", failed.EffectResultStatus);
        var baseResult = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == baseTactic.InstanceId)
            && entry.EffectSegmentIndex == 1);
        Assert.Equal("resolved", baseResult.EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0106")]
    public void CosmosYinMatchingRevealSettlesBuffBeforeTheUnderlyingStackItem()
    {
        var game = Create(7003);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "atomic4-cosmos-success-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        var target = Card("S02-0402", "atomic4-cosmos-success-target");
        owner.Field[0][0] = target;
        var troopsBefore = target.Troops;
        var costBefore = target.CurrentCost;
        owner.Library.Clear();
        var revealed = Card("S01-0109", "atomic4-cosmos-success-top");
        owner.Library.Add(revealed);
        var baseTactic = Card("S01-0219", "atomic4-cosmos-success-base");
        actor.Hand.Add(baseTactic);
        game.State.ActivePlayer = 1;

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);
        PassResponses(game);
        var delayed = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, delayed.ValidChoices);
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == baseTactic.InstanceId);
        Resolve(game, target.InstanceId);

        Assert.Equal(troopsBefore + revealed.BaseTroops, target.Troops);
        Assert.Equal(costBefore + revealed.CurrentCost, target.CurrentCost);
        var counterResults = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId)).ToArray();
        Assert.True(counterResults.Length == 2,
            string.Join(" | ", game.State.Events.TakeLast(20).Select(entry =>
                $"{entry.Type}:{entry.Text}:{entry.EffectResultStatus}:{entry.EffectSegmentIndex}")));
        Assert.All(counterResults, result => Assert.Equal("resolved", result.EffectResultStatus));
        var buffResultIndex = game.State.Events.FindIndex(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId)
            && entry.EffectSegmentIndex == 2);
        var baseResultIndex = game.State.Events.FindIndex(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == baseTactic.InstanceId));
        Assert.True(buffResultIndex >= 0 && baseResultIndex > buffResultIndex);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0106")]
    public void CosmosYinMissReturnsTheCardAndNeverCreatesABuffSegment()
    {
        var game = Create(7004);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "atomic4-cosmos-miss-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        owner.Field[0][0] = Card("S02-0402", "atomic4-cosmos-miss-target");
        owner.Library.Clear();
        var miss = Card("S01-0005", "atomic4-cosmos-miss-top");
        owner.Library.Add(miss);
        var baseTactic = Card("S01-0219", "atomic4-cosmos-miss-base");
        actor.Hand.Add(baseTactic);
        game.State.ActivePlayer = 1;

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Contains(owner.Library, card => card.InstanceId == miss.InstanceId);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == miss.InstanceId);
        Assert.DoesNotContain(game.State.PendingActivations,
            activation => activation.Ability == "composite-segment-declaration");
        var counterResult = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId));
        Assert.Equal("不符合并置底", counterResult.EffectBranchLabel);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0106")]
    public void CosmosYinMatchingRevealWithoutOwnLegionSkipsBuffBeforeUnderlyingSettlement()
    {
        var game = Create(7005);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "atomic4-cosmos-no-target-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        owner.Library.Clear();
        var revealed = Card("S01-0109", "atomic4-cosmos-no-target-top");
        owner.Library.Add(revealed);
        var baseTactic = Card("S01-0219", "atomic4-cosmos-no-target-base");
        actor.Hand.Add(baseTactic);
        game.State.ActivePlayer = 1;

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("declarationTiming") == "post-hidden-reveal");
        Assert.Contains(owner.Graveyard, card => card.InstanceId == revealed.InstanceId);
        var counterResults = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId)).ToArray();
        Assert.True(counterResults.Length == 2,
            string.Join(" | ", game.State.Events.TakeLast(20).Select(entry =>
                $"{entry.Type}:{entry.Text}:{entry.EffectResultStatus}:{entry.EffectSegmentIndex}")));
        Assert.Equal("resolved", counterResults[0].EffectResultStatus);
        Assert.Equal("skipped", counterResults[1].EffectResultStatus);
        var skippedIndex = game.State.Events.FindIndex(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId)
            && entry.EffectResultStatus == "skipped");
        var baseResultIndex = game.State.Events.FindIndex(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == baseTactic.InstanceId));
        Assert.True(skippedIndex >= 0 && baseResultIndex > skippedIndex);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0106")]
    public void NegatedCosmosYinDoesNotRevealTheLibraryOrStartItsLaterSegment()
    {
        var game = Create(7006);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "atomic4-cosmos-negated-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        owner.Field[0][0] = Card("S02-0402", "atomic4-cosmos-negated-target");
        owner.Library.Clear();
        var hiddenTop = Card("S01-0109", "atomic4-cosmos-negated-top");
        owner.Library.Add(hiddenTop);
        var negate = Card("S01-0016", "atomic4-cosmos-negate");
        negate.Hidden = true;
        negate.SetRound = 2;
        actor.Field[1][0] = negate;
        var discardCost = Card("S01-0004", "atomic4-cosmos-negate-cost");
        actor.Hand.Add(discardCost);
        var baseTactic = Card("S01-0219", "atomic4-cosmos-negated-base");
        actor.Hand.Add(baseTactic);
        game.State.ActivePlayer = 1;

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);
        Resolve(game, "pass");
        var negatePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(negate.InstanceId, negatePrompt.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: negatePrompt.PromptId,
            Choice: negate.InstanceId)).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            Choice: discardCost.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Contains(owner.Library, card => card.InstanceId == hiddenTop.InstanceId);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == hiddenTop.InstanceId);
        Assert.Empty(game.State.PendingActivations);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId));
        Assert.Equal("negated", result.EffectResultStatus);
    }
}
