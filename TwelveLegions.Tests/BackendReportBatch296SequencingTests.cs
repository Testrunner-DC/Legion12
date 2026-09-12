using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackendReportBatch296SequencingTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "backend-report-batch296-sequencing", "BATCH296SEQ", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null, int? cost = null)
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
            Cost = cost ?? definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S01-01C1", InstanceId = instanceId };

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source, string trigger)
    {
        if (trigger == "attack")
        {
            game.State.PendingDefense = new L12PendingDefense
            {
                AttackerPlayer = 0,
                AttackerInstanceId = source.InstanceId,
                Target = new L12AttackTarget("master"),
                Stage = L12CombatStage.DefenseChoice,
            };
        }
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "BATCH296时序回归", null,
            new Dictionary<string, string>());
    }

    private static L12Prompt Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = choices.Length == 1
            ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choices[0])
            : new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    [Theory]
    [InlineData("attack")]
    [InlineData("death")]
    [Trait("L12Evidence", "bug:BUG-20260908-59c0bd5d+BUG-20260908-67be4964+BUG-20260908-583ad30e")]
    public void ThutmoseDeclaresTheKillFromPostDebuffTroopsForBothTriggers(string trigger)
    {
        var game = Create(trigger == "attack" ? 29621 : 29622);
        var source = Card("S01-0201", $"batch296-thutmose-{trigger}");
        var becomesEligible = Card("S01-0302", $"batch296-thutmose-{trigger}-2000", troops: 2000);
        var remainsIneligible = Card("S01-0202", $"batch296-thutmose-{trigger}-3000", troops: 3000);
        if (trigger == "attack") game.State.Players[0].Field[0][0] = source;
        else game.State.Players[0].Graveyard.Add(source);
        game.State.Players[1].Field[0][0] = becomesEligible;
        game.State.Players[1].Field[0][1] = remainsIneligible;

        QueueTrigger(game, source, trigger);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("thutmose-debuff", first.Data.GetValueOrDefault("atomicFlow"));
        PassResponses(game);

        Assert.Equal(1000, becomesEligible.Troops);
        Assert.Equal(2000, remainsIneligible.Troops);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", target.Continuation);
        Assert.Contains(becomesEligible.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(remainsIneligible.InstanceId, target.ValidChoices);
        Resolve(game, becomesEligible.InstanceId);

        var second = Assert.Single(game.State.EffectStack);
        Assert.Equal("thutmose-kill", second.Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal(becomesEligible.InstanceId, second.Data.GetValueOrDefault("declared:killTarget"));
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        if (trigger == "death")
        {
            Assert.Contains(source, game.State.Players[0].Graveyard);
            Assert.DoesNotContain(source, game.State.Players[0].Field.SelectMany(row => row));
        }
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-e5ac4d7c")]
    public void HondaAlwaysAppliesTheDebuffThenDeclaresOnlyCurrentZeroCostTargets()
    {
        var game = Create(29623);
        var source = Card("S01-0401", "batch296-honda");
        var becomesZero = Card("S01-0302", "batch296-honda-cost-one", cost: 1);
        var remainsOne = Card("S01-0202", "batch296-honda-cost-two", cost: 2);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = becomesZero;
        game.State.Players[1].Field[0][1] = remainsOne;

        QueueTrigger(game, source, "attack");

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("honda-debuff", first.Data.GetValueOrDefault("atomicFlow"));
        PassResponses(game);

        Assert.Equal(0, becomesZero.CurrentCost);
        Assert.Equal(1, remainsOne.CurrentCost);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", target.Continuation);
        Assert.Contains(becomesZero.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(remainsOne.InstanceId, target.ValidChoices);
        Resolve(game, becomesZero.InstanceId);

        Assert.Equal("honda-kill", Assert.Single(game.State.EffectStack).Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
    }

    [Fact]
    [Trait("L12Evidence", "ruling:R2-S01-0118")]
    public void MarchWithoutAFrontLegionStillOffersAndPaysItsIndependentKillSegment()
    {
        var game = Create(29624);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", "batch296-march-no-front");
        var firstMorale = Morale("batch296-march-morale-1");
        var secondMorale = Morale("batch296-march-morale-2");
        var killTarget = Card("S01-0402", "batch296-march-target", troops: 6000);
        player.Hand.Add(march);
        player.Morale.AddRange([firstMorale, secondMorale]);
        enemy.Field[0][0] = killTarget;

        var play = game.Handle(0, new L12Command("playCard", march.InstanceId));

        Assert.True(play.Accepted, play.Error);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("march-buff-segment", first.Data.GetValueOrDefault("atomicFlow"));
        Assert.Empty(first.Targets);
        PassResponses(game);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:use", mode.ValidChoices);
        Assert.Contains("mode:none", mode.ValidChoices);
        Resolve(game, "mode:use");
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(firstMorale.InstanceId, payment.ValidChoices);
        Assert.Contains(secondMorale.InstanceId, payment.ValidChoices);
        Resolve(game, firstMorale.InstanceId, secondMorale.InstanceId);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(killTarget.InstanceId, target.ValidChoices);
        Resolve(game, killTarget.InstanceId);

        Assert.Empty(player.Morale);
        var second = Assert.Single(game.State.EffectStack);
        Assert.Equal("march-kill-segment", second.Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        PassResponses(game);
        Assert.Contains(killTarget, enemy.Graveyard);
        Assert.Contains(march, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "entry:state-based-death-before-segment-declaration")]
    public void ThutmoseRemovesZeroTroopLegionsBeforeBuildingTheLateTargetPrompt()
    {
        var game = Create(29625);
        var source = Card("S01-0201", "batch296-thutmose-state-check");
        var dies = Card("S01-0402", "batch296-thutmose-dies", troops: 1000);
        var becomesEligible = Card("S01-0302", "batch296-thutmose-new-target", troops: 2000);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = dies;
        game.State.Players[1].Field[0][1] = becomesEligible;

        QueueTrigger(game, source, "death");
        PassResponses(game);

        Assert.Contains(dies, game.State.Players[1].Graveyard);
        Assert.DoesNotContain(dies, game.State.Players[1].Field.SelectMany(row => row));
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(becomesEligible.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(dies.InstanceId, target.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "entry:thutmose-negated-debuff-keeps-independent-kill")]
    public void NegatedThutmoseDebuffStillOffersOnlyTargetsLegalInTheUnchangedState()
    {
        var game = Create(29636);
        var source = Card("S01-0201", "batch296-thutmose-negated");
        var alreadyEligible = Card("S01-0402", "batch296-thutmose-negated-1000", troops: 1000);
        var needsDebuff = Card("S01-0302", "batch296-thutmose-negated-2000", troops: 2000);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = alreadyEligible;
        game.State.Players[1].Field[0][1] = needsDebuff;

        QueueTrigger(game, source, "attack");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("thutmose-debuff", first.Data.GetValueOrDefault("atomicFlow"));
        first.Negated = true;
        PassResponses(game);

        Assert.Equal(1000, alreadyEligible.Troops);
        Assert.Equal(2000, needsDebuff.Troops);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(alreadyEligible.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(needsDebuff.InstanceId, target.ValidChoices);
        Resolve(game, alreadyEligible.InstanceId);

        var second = Assert.Single(game.State.EffectStack);
        Assert.Equal("thutmose-kill", second.Data.GetValueOrDefault("atomicFlow"));
        Assert.NotEqual(first.StackItemId, second.StackItemId);
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        PassResponses(game);
        Assert.Contains(alreadyEligible, game.State.Players[1].Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "entry:state-check-trigger-barrier-before-late-declaration")]
    public void ThutmoseWaitsForARealDeathTriggerBeforeDeclaringItsLateTarget()
    {
        var game = Create(29637);
        var source = Card("S01-0201", "batch296-thutmose-barrier");
        var wu = Card("S01-0102", "batch296-thutmose-barrier-wu", troops: 1000);
        var becomesEligible = Card("S01-0302", "batch296-thutmose-barrier-target", troops: 2000);
        var drawn = Card("S01-0001", "batch296-thutmose-barrier-draw");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Hp = 5;
        game.State.Players[1].Library.Add(drawn);
        game.State.Players[1].Field[0][0] = wu;
        game.State.Players[1].Field[0][1] = becomesEligible;

        QueueTrigger(game, source, "death");
        while (game.State.EffectStack.Any(item =>
                   item.Data.GetValueOrDefault("atomicFlow") == "thutmose-debuff"))
        {
            var response = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", response.Kind);
            Resolve(game, "pass");
        }

        Assert.Contains(wu, game.State.Players[1].Graveyard);
        Assert.True(game.State.EffectStack.Concat(game.State.DeferredEffectStack).Any(item =>
                item.Data.GetValueOrDefault("atomicFlow") == "composite-state-check-barrier"),
            $"stack={string.Join('|', game.State.EffectStack.Select(item => item.Trigger + ':' + item.Data.GetValueOrDefault("atomicFlow")))}; " +
            $"deferred={string.Join('|', game.State.DeferredEffectStack.Select(item => item.Trigger + ':' + item.Data.GetValueOrDefault("atomicFlow")))}; " +
            $"batches={game.State.PendingTriggerBatches.Count}; candidates={game.State.PendingTriggerStackCandidates.Count}; " +
            $"prompts={string.Join('|', game.State.PendingPrompts.Select(prompt => prompt.Kind + ':' + prompt.Continuation))}");
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == wu.InstanceId
            && item.Trigger == "death");
        PassResponses(game);

        Assert.Equal(6, game.State.Players[1].Hp);
        Assert.Contains(drawn, game.State.Players[1].Hand);
        Assert.DoesNotContain(game.State.DeferredEffectStack, item =>
            item.Data.GetValueOrDefault("atomicFlow") == "composite-state-check-barrier");
        var lateTarget = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(becomesEligible.InstanceId, lateTarget.ValidChoices);
        Assert.DoesNotContain(wu.InstanceId, lateTarget.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "entry:late-trigger-checkpoint-and-idempotency")]
    public void ThutmoseLateDeclarationSurvivesCheckpointAndRejectsTheOldPromptTwice()
    {
        var game = Create(29626);
        var source = Card("S01-0201", "batch296-thutmose-restore");
        var target = Card("S01-0302", "batch296-thutmose-restore-target", troops: 1000);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;

        QueueTrigger(game, source, "death");
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);
        var oldPrompt = Assert.Single(game.State.PendingPrompts);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredPrompt = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal(oldPrompt.PromptId, restoredPrompt.PromptId);
        Resolve(restored, target.InstanceId);

        var duplicate = restored.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId, Choice: target.InstanceId));
        Assert.False(duplicate.Accepted);
        Assert.Single(restored.State.EffectStack, item =>
            item.Data.GetValueOrDefault("atomicFlow") == "thutmose-kill");
    }

    [Fact]
    [Trait("L12Evidence", "entry:mandatory-honda-first-segment")]
    public void HondaWithoutAZeroCostTargetKeepsTheMandatoryDebuffAndCreatesNoEmptyPrompt()
    {
        var game = Create(29627);
        var source = Card("S01-0401", "batch296-honda-no-target");
        var enemy = Card("S01-0202", "batch296-honda-cost-three", cost: 3);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = enemy;

        QueueTrigger(game, source, "attack");
        PassResponses(game);

        Assert.Equal(2, enemy.CurrentCost);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(game.State.EffectStack.Concat(game.State.DeferredEffectStack), item =>
            item.Data.GetValueOrDefault("atomicFlow") == "honda-kill");
        Assert.Same(source, game.State.Players[0].Field[0][0]);
    }

    [Fact]
    [Trait("L12Evidence", "entry:field-trigger-late-cancel-is-local")]
    public void HondaLateTargetInvalidationDoesNotUndoTheDebuffOrMoveItsFieldSource()
    {
        var game = Create(29628);
        var source = Card("S01-0401", "batch296-honda-invalid-target");
        var target = Card("S01-0302", "batch296-honda-invalid-target-enemy", cost: 1);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;

        QueueTrigger(game, source, "attack");
        PassResponses(game);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(target);

        var invalid = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId, Choice: target.InstanceId));

        Assert.True(invalid.Accepted, invalid.Error);
        Assert.Equal(0, target.CurrentCost);
        Assert.Same(source, game.State.Players[0].Field[0][0]);
        Assert.DoesNotContain(source, game.State.Players[0].Graveyard);
        Assert.DoesNotContain(game.State.EffectStack.Concat(game.State.DeferredEffectStack), item =>
            item.Data.GetValueOrDefault("atomicFlow") == "honda-kill");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "entry:march-unavailable-followup-has-no-empty-prompt")]
    public void MarchSkipsOnlyTheUnavailableFollowupWithoutAnEmptyDecision(int moraleCount, bool addTarget)
    {
        var game = Create(29629 + moraleCount);
        var player = game.State.Players[0];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", $"batch296-march-unavailable-{moraleCount}-{addTarget}");
        player.Hand.Add(march);
        for (var index = 0; index < moraleCount; index++)
            player.Morale.Add(Morale($"batch296-march-unavailable-morale-{moraleCount}-{index}"));
        if (addTarget)
            game.State.Players[1].Field[0][0] = Card("S01-0402", "batch296-march-unavailable-target", 6000);

        Assert.True(game.Handle(0, new L12Command("playCard", march.InstanceId)).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(game.State.EffectStack.Concat(game.State.DeferredEffectStack), item =>
            item.Data.GetValueOrDefault("atomicFlow") == "march-kill-segment");
        Assert.Contains(march, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "entry:march-paid-target-invalidity-does-not-refund")]
    public void MarchDoesNotRefundTheCommittedReturnWhenItsLateTargetLeavesBeforeResolution()
    {
        var game = Create(29632);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", "batch296-march-paid-invalid");
        var front = Card("S01-0109", "batch296-march-paid-invalid-front");
        var target = Card("S01-0402", "batch296-march-paid-invalid-target", troops: 6000);
        player.Hand.Add(march);
        player.Field[0][0] = front;
        player.Morale.AddRange([Morale("batch296-march-paid-invalid-m1"), Morale("batch296-march-paid-invalid-m2")]);
        opponent.Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("playCard", march.InstanceId)).Accepted);
        Resolve(game, front.InstanceId);
        PassResponses(game);
        Resolve(game, "mode:use");
        var payment = Assert.Single(game.State.PendingPrompts);
        Resolve(game, payment.ValidChoices[0], payment.ValidChoices[1]);
        Resolve(game, target.InstanceId);
        Assert.Empty(player.Morale);
        Assert.Equal(front.BaseTroops + 2000, front.Troops);

        opponent.Field[0][0] = null;
        opponent.Graveyard.Add(target);
        PassResponses(game);

        Assert.Empty(player.Morale);
        Assert.Equal(front.BaseTroops + 2000, front.Troops);
        Assert.Contains(march, player.Graveyard);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == march.InstanceId)
            && entry.EffectSegmentIndex == 2);
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("已离场", StringComparison.Ordinal)
            && entry.Text.Contains("不恢复", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:selected-threshold-target-is-revalidated-after-responses")]
    public void MarchSelectedTargetThatRisesAboveItsTroopLimitFailsAtSettlementWithoutRefund()
    {
        var game = Create(296321);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", "batch296-march-threshold-failed");
        var target = Card("S01-0402", "batch296-march-threshold-target", troops: 6000);
        player.Hand.Add(march);
        player.Morale.AddRange([
            Morale("batch296-march-threshold-m1"),
            Morale("batch296-march-threshold-m2"),
        ]);
        opponent.Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("playCard", march.InstanceId)).Accepted);
        PassResponses(game);
        Resolve(game, "mode:use");
        var payment = Assert.Single(game.State.PendingPrompts);
        Resolve(game, payment.ValidChoices[0], payment.ValidChoices[1]);
        Resolve(game, target.InstanceId);

        var kill = Assert.Single(game.State.EffectStack, item =>
            item.Data.GetValueOrDefault("atomicFlow") == "march-kill-segment");
        Assert.Equal(target.InstanceId, kill.Data.GetValueOrDefault("declared:killTarget"));
        Assert.Empty(player.Morale);

        // 模拟逆序结算的响应效果先令目标兵力 +1000。
        target.Troops += 1000;
        PassResponses(game);

        Assert.Same(target, opponent.Field[0][0]);
        Assert.DoesNotContain(target, opponent.Graveyard);
        Assert.Empty(player.Morale);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == march.InstanceId)
            && entry.EffectSegmentIndex == 2);
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("当前兵力已高于6000", StringComparison.Ordinal)
            && entry.Text.Contains("不恢复", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:march-invalid-before-stack-does-not-commit-cost")]
    public void MarchDoesNotCommitItsReturnWhenTheLateTargetInvalidatesBeforeStacking()
    {
        var game = Create(29638);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", "batch296-march-prestack-invalid");
        var firstMorale = Morale("batch296-march-prestack-invalid-m1");
        var secondMorale = Morale("batch296-march-prestack-invalid-m2");
        var target = Card("S01-0402", "batch296-march-prestack-invalid-target", troops: 6000);
        player.Hand.Add(march);
        player.Morale.AddRange([firstMorale, secondMorale]);
        opponent.Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("playCard", march.InstanceId)).Accepted);
        PassResponses(game);
        Resolve(game, "mode:use");
        Resolve(game, firstMorale.InstanceId, secondMorale.InstanceId);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        opponent.Field[0][0] = null;
        opponent.Graveyard.Add(target);

        var stale = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId, Choice: target.InstanceId));

        Assert.True(stale.Accepted, stale.Error);
        Assert.Equal(2, player.Morale.Count);
        Assert.DoesNotContain(game.State.EffectStack.Concat(game.State.DeferredEffectStack), item =>
            item.Data.GetValueOrDefault("atomicFlow") == "march-kill-segment");
        Assert.Contains(march, player.Graveyard);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == march.InstanceId)
            && entry.EffectSegmentIndex == 2);
        Assert.Equal("failed", result.EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "entry:march-negated-first-segment-still-has-late-segment")]
    public void NegatedMarchBuffStillOffersTheIndependentPaidKillSegment()
    {
        var game = Create(29633);
        var player = game.State.Players[0];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", "batch296-march-negated-first");
        var front = Card("S01-0109", "batch296-march-negated-first-front");
        var target = Card("S01-0402", "batch296-march-negated-first-target", troops: 6000);
        player.Hand.Add(march);
        player.Field[0][0] = front;
        player.Morale.AddRange([Morale("batch296-march-negated-first-m1"), Morale("batch296-march-negated-first-m2")]);
        game.State.Players[1].Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("playCard", march.InstanceId)).Accepted);
        Resolve(game, front.InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Equal(front.BaseTroops, front.Troops);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:use", mode.ValidChoices);
        Assert.Contains("mode:none", mode.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "entry:march-effect-only-repeat-does-not-repay-cost")]
    public void RepeatedMarchEffectOffersItsIndependentKillSegmentWithoutMorale()
    {
        var game = Create(29635);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var repeatedMarch = Card("S01-0118", "batch296-march-repeat");
        var front = Card("S01-0109", "batch296-march-repeat-front");
        var target = Card("S01-0402", "batch296-march-repeat-target", troops: 6000);
        player.Field[0][0] = front;
        opponent.Field[0][0] = target;

        var begin = (CommandResult)Invoke(game, "BeginRepeatedCompositeEffectDeclaration", 0, repeatedMarch)!;

        Assert.True(begin.Accepted, begin.Error);
        Resolve(game, front.InstanceId);
        Assert.Equal("true", Assert.Single(game.State.EffectStack).Data.GetValueOrDefault("repeatedEffectOnly"));
        PassResponses(game);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:use", mode.ValidChoices);
        Resolve(game, "mode:use");
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, targetPrompt.ValidChoices);
        Assert.Empty(player.Morale);
        Resolve(game, target.InstanceId);

        var kill = Assert.Single(game.State.EffectStack);
        Assert.Equal("march-kill-segment", kill.Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal("true", kill.Data.GetValueOrDefault("repeatedEffectOnly"));
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        PassResponses(game);
        Assert.Contains(target, opponent.Graveyard);
        Assert.DoesNotContain(repeatedMarch, player.Resolving.Concat(player.Graveyard));
    }

    [Fact]
    [Trait("L12Evidence", "entry:march-late-checkpoint-and-idempotency")]
    public void MarchLateDecisionSurvivesCheckpointAndRejectsARepeatedOldPrompt()
    {
        var game = Create(29634);
        var player = game.State.Players[0];
        player.FreeTacticCount = 1;
        var march = Card("S01-0118", "batch296-march-restore");
        var target = Card("S01-0402", "batch296-march-restore-target", troops: 6000);
        player.Hand.Add(march);
        player.Morale.AddRange([Morale("batch296-march-restore-m1"), Morale("batch296-march-restore-m2")]);
        game.State.Players[1].Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("playCard", march.InstanceId)).Accepted);
        PassResponses(game);
        var oldMode = Assert.Single(game.State.PendingPrompts);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        var restoredMode = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal(oldMode.PromptId, restoredMode.PromptId);
        Resolve(restored, "mode:use");
        var duplicate = restored.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredMode.PromptId, Choice: "mode:use"));

        Assert.False(duplicate.Accepted);
        var payment = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal(2, payment.MinChoose);
        Assert.Contains("batch296-march-restore-m1", payment.ValidChoices);
        Assert.Contains("batch296-march-restore-m2", payment.ValidChoices);
    }
}
