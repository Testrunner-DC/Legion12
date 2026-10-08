using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterPendingWorkClosureRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [Trait("L12Evidence", "rule:disaster-after-complete-effect-work")]
    public void ThresholdDisasterWaitsForDelegatedDeclarationResponseAndResolution(int controller, int restoreAt)
    {
        var scene = CreateScene(controller, 1, 2026100910 + controller * 3 + restoreAt);
        var game = scene.Game;
        PlayAndDeclareRamses(game, controller, scene.RamsesId, scene.DelegateIds);
        if (restoreAt == 1) game = Restore(game);
        PassResponses(game);

        Assert.Null(game.State.ActiveDisaster);
        Assert.True(game.State.CheckDisasterAfterStack);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(scene.VictimIds[0], declaration.ValidChoices);
        Assert.Single(game.State.PendingActivations);
        if (restoreAt == 2)
        {
            game = Restore(game);
            declaration = Assert.Single(game.State.PendingPrompts);
            Assert.Null(game.State.ActiveDisaster);
        }
        AssertInvalidChoiceDoesNotAdvance(game, declaration);

        var command = new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: scene.VictimIds[0]);
        Assert.True(game.Handle(controller, command).Accepted);
        Assert.False(game.Handle(controller, command).Accepted);
        Assert.NotNull(game.State.ResponseWindow);
        Assert.Null(game.State.ActiveDisaster);
        Assert.Contains(scene.VictimIds[0], game.State.Players[1 - controller].Field
            .SelectMany(row => row).Where(card => card is not null).Select(card => card!.InstanceId));
        PassResponses(game);
        DrainRemainingPrompts(game);

        Assert.Contains(game.State.Players[1 - controller].Graveyard,
            card => card.InstanceId == scene.VictimIds[0]);
        var leave = Assert.Single(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == scene.VictimIds[0]));
        var disaster = Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        Assert.True(leave.Sequence < disaster.Sequence, "The delegated kill must complete before the disaster.");
        AssertClosed(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "rule:disaster-after-complete-effect-work")]
    public void TwoDelegatedEffectsBothCompleteBeforeOneDisaster(int controller)
    {
        var scene = CreateScene(controller, 2, 2026100940 + controller);
        var game = scene.Game;
        PlayAndDeclareRamses(game, controller, scene.RamsesId, scene.DelegateIds);
        PassResponses(game);
        var killed = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            Assert.Null(game.State.ActiveDisaster);
            Assert.True(game.State.CheckDisasterAfterStack);
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("pending-activation", prompt.Continuation);
            var victim = scene.VictimIds.First(id => !killed.Contains(id) && prompt.ValidChoices.Contains(id));
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: victim)).Accepted);
            Assert.Null(game.State.ActiveDisaster);
            PassResponses(game);
            killed.Add(victim);
            Assert.Contains(game.State.Players[1 - controller].Graveyard, card => card.InstanceId == victim);
            if (index == 0)
            {
                Assert.Null(game.State.ActiveDisaster);
                game = Restore(game);
            }
        }
        DrainRemainingPrompts(game);
        var disaster = Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        Assert.All(killed, id => Assert.True(Assert.Single(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == id)).Sequence < disaster.Sequence));
        AssertClosed(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "rule:disaster-after-canceled-effect-work")]
    public void ParentDeclarationDeclinedDoesNotPermanentlyBlockScheduledDisaster(int controller)
    {
        var scene = CreateScene(controller, 1, 2026100950 + controller);
        var game = scene.Game;
        Assert.True(game.Handle(controller,
            new L12Command("playCard", scene.RamsesId, Row: 0, Slot: 0)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "skip")).Accepted);
        DrainRemainingPrompts(game);
        Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => scene.DelegateIds.Contains(card.InstanceId)));
        AssertClosed(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "rule:disaster-after-invalidated-effect-work")]
    public void InvalidatedDeclaredTargetSettlesItsWorkBeforeDisasterWithoutSubstitution(int controller)
    {
        var scene = CreateScene(controller, 1, 2026100960 + controller);
        var game = scene.Game;
        PlayAndDeclareRamses(game, controller, scene.RamsesId, scene.DelegateIds);
        PassResponses(game);
        Assert.Null(game.State.ActiveDisaster);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: scene.VictimIds[0])).Accepted);
        var opponent = game.State.Players[1 - controller];
        var target = opponent.Field[0][0]!;
        opponent.Field[0][0] = null;
        opponent.Removed.Add(target);
        PassResponses(game);
        DrainRemainingPrompts(game);
        Assert.Contains(opponent.Removed, card => card.InstanceId == target.InstanceId);
        Assert.DoesNotContain(opponent.Graveyard, card => card.InstanceId == target.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        AssertClosed(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "rule:disaster-after-declined-work")]
    public void ExistingDelegatedDeclarationDeclineEndsOnlyItsOwnWorkAndReleasesDisaster(int controller)
    {
        // Preserve the existing declaration cancellation contract; this test does
        // not introduce a new card-optionality ruling.
        var scene = CreateScene(controller, 1, 2026100970 + controller);
        var game = scene.Game;
        PlayAndDeclareRamses(game, controller, scene.RamsesId, scene.DelegateIds);
        PassResponses(game);
        Assert.Null(game.State.ActiveDisaster);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "skip")).Accepted);
        DrainRemainingPrompts(game);
        Assert.Contains(game.State.Players[1 - controller].Field.SelectMany(row => row),
            card => card?.InstanceId == scene.VictimIds[0]);
        Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        AssertClosed(game);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [Trait("L12Evidence", "rule:state-death-work-before-disaster")]
    public void LastDelegatedKillCausingAuraLossCompletesStateDeathAndItsTriggerBeforeDisaster(
        int controller, int responsePath)
    {
        var scene = CreateScene(controller, 1, 2026100980 + controller);
        var game = scene.Game;
        var opponent = game.State.Players[1 - controller];
        var hannibal = Card("S02-0516", $"work-hannibal-{controller}", 1 - controller);
        var mozi = Card("S01-0110", $"work-mozi-{controller}", 1 - controller);
        // Existing damage leaves Mozi at 500 while Hannibal grants 1000.
        // Removing Hannibal makes Mozi lethally low by continuous recalculation.
        mozi.Troops = -500;
        opponent.Field[0][0] = hannibal;
        opponent.Field[0][1] = mozi;
        if (responsePath != 0)
        {
            Assert.True(game.ApplyResponsePreference(0, L12GameEngine.InvalidFiveSecondsResponseMode).Accepted);
            Assert.True(game.ApplyResponsePreference(1, L12GameEngine.InvalidFiveSecondsResponseMode).Accepted);
        }
        PlayAndDeclareRamses(game, controller, scene.RamsesId, scene.DelegateIds);
        Assert.Equal(500, mozi.Troops);
        PassResponses(game);
        Assert.Null(game.State.ActiveDisaster);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(hannibal.InstanceId, prompt.ValidChoices);
        Assert.True(game.Handle(controller, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: hannibal.InstanceId)).Accepted);
        if (responsePath == 0)
        {
            PassResponses(game);
        }
        else
        {
            var lease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
            var revision = game.State.Revision;
            Assert.True(game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
                lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc));
            Assert.Equal(revision + 1, game.State.Revision);
            Assert.Null(game.State.ActiveDisaster);
            if (responsePath == 2) game = Restore(game);
            var restored = Restore(game);
            var second = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
            Assert.Equal(second, restored.CaptureResponseAutoCloseLease());
            revision = game.State.Revision;
            Assert.True(game.TryExpireResponseAutoClose(second.PromptId, second.StackItemId,
                second.PriorityPlayer, second.DeadlineUtc, second.DeadlineUtc));
            Assert.True(restored.TryExpireResponseAutoClose(second.PromptId, second.StackItemId,
                second.PriorityPlayer, second.DeadlineUtc, second.DeadlineUtc));
            Assert.Equal(revision + 1, game.State.Revision);
            Assert.Equal(game.SerializeFullState(), restored.SerializeFullState());
            Assert.False(game.TryExpireResponseAutoClose(second.PromptId, second.StackItemId,
                second.PriorityPlayer, second.DeadlineUtc, second.DeadlineUtc));
            Assert.Equal(game.SerializeFullState(), restored.SerializeFullState());
        }
        DrainRemainingPrompts(game);
        var disaster = Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        var moziDeath = Assert.Single(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == mozi.InstanceId));
        var moziTrigger = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.InstanceId == mozi.InstanceId));
        Assert.True(moziDeath.Sequence < disaster.Sequence, "State-based death is current pending work.");
        Assert.True(moziTrigger.Sequence < disaster.Sequence, "The death trigger must precede the disaster.");
        AssertClosed(game);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "rule:composite-tail-state-death-before-disaster")]
    public void UnrespondableCompositeTailRegistersAuraLossDeathBeforeDisaster(int controller, bool restore)
    {
        var scene = CreateScene(controller, 1, 2026100995 + controller);
        var game = scene.Game;
        var owner = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        owner.Hand.Clear();
        owner.Field[0] = new L12CardInstance?[3];
        owner.Field[1] = new L12CardInstance?[3];
        var hijikata = Card("S01-0406", $"work-hijikata-{controller}", controller);
        owner.Hand.Add(hijikata);
        game.State.DisasterValue = 8;
        var broad = Card("S01-0004", $"work-broad-{controller}", 1 - controller);
        var hannibal = Card("S02-0516", $"work-tail-hannibal-{controller}", 1 - controller);
        hannibal.CostModifier = -2;
        var mozi = Card("S01-0110", $"work-tail-mozi-{controller}", 1 - controller);
        mozi.Troops = -500;
        opponent.Field[0] = [broad, hannibal, mozi];
        Assert.True(game.Handle(controller, new L12Command("playCard", hijikata.InstanceId,
            Row: 0, Slot: 0)).Accepted);
        Assert.Equal(500, mozi.Troops);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(broad.InstanceId, declaration.ValidChoices);
        Assert.Contains(hannibal.InstanceId, declaration.ValidChoices);
        Assert.True(game.Handle(controller, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: [broad.InstanceId, hannibal.InstanceId])).Accepted);
        if (restore) game = Restore(game);
        Assert.Null(game.State.ActiveDisaster);
        PassResponses(game);
        DrainRemainingPrompts(game);
        var disaster = Assert.Single(game.State.Events, entry => entry.Type == "disaster");
        var death = Assert.Single(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == mozi.InstanceId));
        var trigger = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.InstanceId == mozi.InstanceId));
        Assert.True(death.Sequence < disaster.Sequence, "The pumped composite tail must finish its state deaths.");
        Assert.True(trigger.Sequence < disaster.Sequence, "The pumped tail death trigger is still current work.");
        AssertClosed(game);
    }

    [Fact]
    [Trait("L12Evidence", "rule:effect-work-exception-cleanup")]
    public void FailedSynchronousWorkReleasesTransientBarrierWithoutAdvancingPartialState()
    {
        var scene = CreateScene(0, 1, 2026100990);
        var game = scene.Game;
        var before = game.SerializeFullState();
        var boundary = typeof(L12GameEngine).GetMethod("WithinEffectWorkDispatch",
            BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(bool));
        var request = typeof(L12GameEngine).GetMethod("RequestStackProgress",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Func<bool> failedWork = () =>
        {
            request.Invoke(game, [true]);
            throw new InvalidOperationException("synthetic resolver failure");
        };
        var error = Assert.Throws<TargetInvocationException>(() => boundary.Invoke(game, [failedWork]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(before, game.SerializeFullState());
        Assert.Equal(0, typeof(L12GameEngine).GetField("_effectWorkDispatchDepth",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game));
        Assert.Equal(false, typeof(L12GameEngine).GetField("_stackProgressRequested",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game));
        Assert.Equal(false, typeof(L12GameEngine).GetField("_stackProgressDraining",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game));
        Assert.Equal(false, typeof(L12GameEngine).GetField("_stackProgressSuppressStateDeathTriggers",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game));
        PlayAndDeclareRamses(game, 0, scene.RamsesId, scene.DelegateIds);
        PassResponses(game);
        Assert.Null(game.State.ActiveDisaster);
        Assert.Single(game.State.PendingPrompts);
    }

    private static Scene CreateScene(int controller, int delegateCount, int seed)
    {
        var game = new L12GameEngine(Catalog, "disaster-pending-work", "DISASTER-WORK", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, disasterMode: "all", stateFormatVersion: 2);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        game.State.RemovedDisasters.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            for (var index = 0; index < 8; index++)
                player.Library.Add(Card("S01-0002", $"work-lib-{seed}-{player.PlayerIndex}-{index}", player.PlayerIndex));
        }
        var owner = game.State.Players[controller];
        var enemy = game.State.Players[1 - controller];
        var ramses = Card("S01-0202", $"work-ramses-{seed}", controller);
        owner.Hand.Add(ramses);
        var delegates = new List<string>();
        var victims = new List<string>();
        for (var index = 0; index < delegateCount; index++)
        {
            var delegated = Card("S01-0201", $"work-delegate-{seed}-{index}", controller);
            owner.Field[index == 0 ? 0 : 1][index == 0 ? 1 : 2] = delegated;
            delegates.Add(delegated.InstanceId);
            var victim = Card("S01-0002", $"work-victim-{seed}-{index}", 1 - controller);
            enemy.Field[0][index] = victim;
            victims.Add(victim.InstanceId);
        }
        for (var index = 0; index < ramses.Cost; index++)
            owner.Morale.Add(new L12MoraleCard { CardId = "S01-04C1", InstanceId = $"work-morale-{seed}-{index}" });
        game.State.DisasterValue = 6;
        game.State.DisasterDeck.Add(Card("S01-DS07", $"work-apocalypse-{seed}", controller));
        return new(game, ramses.InstanceId, delegates.ToArray(), victims.ToArray());
    }

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new()
        {
            InstanceId = instance, OwnerIndex = owner, CardId = id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void PlayAndDeclareRamses(L12GameEngine game, int controller, string id, string[] delegates)
    {
        Assert.True(game.Handle(controller, new L12Command("playCard", id, Row: 0, Slot: 0)).Accepted);
        Assert.Null(game.State.ActiveDisaster);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.All(delegates, target => Assert.Contains(target, prompt.ValidChoices));
        Assert.True(game.Handle(controller, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: delegates.ToList())).Accepted);
    }

    private static void AssertInvalidChoiceDoesNotAdvance(L12GameEngine game, L12Prompt prompt)
    {
        var original = game.SerializeFullState();
        Assert.False(game.Handle(1 - prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: prompt.ValidChoices[0])).Accepted);
        Assert.Equal(original, game.SerializeFullState());
        Assert.False(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "invalid-object")).Accepted);
        Assert.Equal(original, game.SerializeFullState());
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100; safety++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null || prompt.Kind != "response") return;
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Fail("Response chain did not finish.");
    }

    private static void DrainRemainingPrompts(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var choices = prompt.Continuation == "trigger-batch-order"
                ? prompt.ValidChoices.ToArray()
                : prompt.ValidChoices.Take(Math.Max(1, prompt.MinChoose)).ToArray();
            var command = prompt.Kind == "response"
                ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")
                : prompt.Data.GetValueOrDefault("placementMode") == "all-bottom"
                    ? new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                        BottomCardInstanceIds: choices.ToList())
                : new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList());
            var result = game.Handle(prompt.PlayerIndex, command);
            Assert.True(result.Accepted,
                $"{result.Error}; kind={prompt.Kind}; continuation={prompt.Continuation}; "
                + $"choices={string.Join(',', choices)}; phase={game.State.Phase}");
        }
        Assert.Empty(game.State.PendingPrompts);
    }

    private static void AssertClosed(L12GameEngine game)
    {
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
        Assert.False(game.State.CheckDisasterAfterStack);
    }

    private sealed record Scene(L12GameEngine Game, string RamsesId, string[] DelegateIds, string[] VictimIds);
}
