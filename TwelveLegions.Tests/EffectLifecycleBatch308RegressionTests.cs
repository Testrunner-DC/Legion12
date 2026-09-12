using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectLifecycleBatch308RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, int stateFormatVersion = 1)
    {
        var game = new L12GameEngine(Catalog, "effect-lifecycle-batch308", "BATCH308", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            effectPresentationSnapshot: null, stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
        }
        return game;
    }

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
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void QueueReaction(L12GameEngine game, int controller, L12CardInstance source)
    {
        var candidate = new L12TriggerCandidate
        {
            CandidateId = $"batch308-{source.InstanceId}",
            Controller = controller,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            SourceSnapshot = source.Clone(),
            Trigger = "reaction",
            Text = "【对方进攻后】反击战术",
        };
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });
    }

    private static CommandResult Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return result;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static void CompleteImmortalGiftDeclaration(
        L12GameEngine game, L12CardInstance? guard, bool summon)
    {
        Resolve(game, "mode:use");
        var entryPrompt = Assert.Single(game.State.PendingPrompts);
        if (!summon)
        {
            Resolve(game, "mode:none");
            return;
        }
        Assert.NotNull(guard);
        Resolve(game, guard!.InstanceId);
        while (game.State.PendingActivations.Count > 0)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Resolve(game, prompt.ValidChoices[0]);
        }
    }

    private static L12ActionEvent[] ResultsFor(L12GameEngine game, string sourceInstanceId)
        => game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceInstanceId)).ToArray();

    [Theory]
    [InlineData("S01-0223", "trigger:S01-0223:reaction", "immortal-gift-draw", "immortal-gift-summon")]
    [InlineData("S01-0420", "trigger:S01-0420:reaction", "seppuku-draw", "seppuku-cost")]
    public void MigratedReactionsExposeTwoStableSegmentsOnTheirPrintedAbility(
        string cardId, string planId, string firstFlow, string secondFlow)
    {
        var card = Catalog.AtomicEffects.Find(cardId);
        Assert.NotNull(card);
        var ability = Assert.Single(card.Abilities, candidate => candidate.Sequence == 1);
        var first = Assert.Single(ability.Presentations, scene => scene.Flow == firstFlow);
        var secondScenes = ability.Presentations.Where(scene => scene.Flow == secondFlow).ToArray();
        var second = secondScenes[0];

        Assert.Equal(1, first.SegmentIndex);
        Assert.Equal(2, second.SegmentIndex);
        Assert.Equal(2, first.SegmentCount);
        Assert.Equal(2, second.SegmentCount);
        Assert.Equal(cardId == "S01-0223" ? 2 : 1, secondScenes.Length);
        Assert.StartsWith(L12EffectPresentationVariants.SceneKeyPrefix(planId), first.Trigger,
            StringComparison.Ordinal);
        Assert.NotEqual(first.SceneId, second.SceneId);
        Assert.DoesNotContain("InstanceId", first.DefaultText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InstanceId", second.DefaultText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImmortalGiftDeclinePublishesResolvedDrawAndDeclinedOptionalEntry()
    {
        var game = Create(308001);
        var source = Card("S01-0223", "gift-decline");
        var guard = Card("S01-0212", "gift-decline-guard");
        var drawn = Card("S01-0201", "gift-decline-draw");
        source.Hidden = true;
        game.State.Players[0].Field[1][0] = source;
        game.State.Players[0].Graveyard.Add(guard);
        game.State.Players[0].Library.Add(drawn);

        QueueReaction(game, 0, source);
        var firstPrompt = Assert.Single(game.State.PendingPrompts);
        var firstPromptId = firstPrompt.PromptId;
        Resolve(game, "mode:use");
        var duplicate = game.Handle(firstPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: firstPromptId, Choice: "mode:use"));
        Assert.False(duplicate.Accepted);
        Resolve(game, "mode:none");

        var stack = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == source.InstanceId);
        Assert.Equal("trigger:S01-0223:reaction", stack.Data["compositePlan"]);
        Assert.Equal("immortal-gift-draw", stack.Data["atomicFlow"]);
        Assert.Equal("single-effect", stack.Data["compositeResponseScope"]);
        PassResponses(game);

        Assert.Contains(drawn, game.State.Players[0].Hand);
        var results = ResultsFor(game, source.InstanceId);
        Assert.True(results.Length == 2, LifecycleDiagnostic(game, source.InstanceId));
        Assert.Collection(results.OrderBy(entry => entry.EffectSegmentIndex),
            first =>
            {
                Assert.Equal(1, first.EffectSegmentIndex);
                Assert.Equal("resolved", first.EffectResultStatus);
            },
            second =>
            {
                Assert.Equal(2, second.EffectSegmentIndex);
                Assert.Equal("declined", second.EffectResultStatus);
                Assert.Contains("选择不发动", second.EffectText, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ImmortalGiftRestoreKeepsDeclarationAndSkipsOnlyVanishedGuardSegment()
    {
        var game = Create(308002, stateFormatVersion: 2);
        var player = game.State.Players[0];
        var source = Card("S01-0223", "gift-restore");
        var guard = Card("S01-0212", "gift-restore-guard");
        var drawn = Card("S01-0201", "gift-restore-draw");
        source.Hidden = true;
        player.Field[1][0] = source;
        player.Graveyard.Add(guard);
        player.Library.Add(drawn);

        QueueReaction(game, 0, source);
        CompleteImmortalGiftDeclaration(game, guard, summon: true);
        var sceneId = Assert.Single(game.State.EffectStack).Data["presentationSceneId"];

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restored = Assert.Single(game.State.EffectStack);
        Assert.Equal(guard.InstanceId, restored.Data["declared:entryCard"]);
        Assert.Equal(sceneId, restored.Data["presentationSceneId"]);
        var restoredPlayer = game.State.Players[0];
        var vanished = Assert.Single(restoredPlayer.Graveyard, card => card.InstanceId == guard.InstanceId);
        restoredPlayer.Graveyard.Remove(vanished);
        restoredPlayer.Hand.Add(vanished);

        PassResponses(game);

        Assert.Contains(drawn.InstanceId, restoredPlayer.Hand.Select(card => card.InstanceId));
        Assert.Contains(vanished, restoredPlayer.Hand);
        var results = ResultsFor(game, source.InstanceId).OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.True(results.Length == 2, LifecycleDiagnostic(game, source.InstanceId));
        Assert.Equal(["resolved", "failed"], results.Select(entry => entry.EffectResultStatus));
        Assert.Equal([1, 2], results.Select(entry => entry.EffectSegmentIndex));
    }

    [Fact]
    public void NegatingImmortalGiftFirstSegmentStopsTheWholePrintedEffect()
    {
        var game = Create(308003);
        var player = game.State.Players[0];
        var source = Card("S01-0223", "gift-negated");
        var guard = Card("S01-0212", "gift-negated-guard");
        var drawn = Card("S01-0201", "gift-negated-draw");
        source.Hidden = true;
        player.Field[1][0] = source;
        player.Graveyard.Add(guard);
        player.Library.Add(drawn);

        QueueReaction(game, 0, source);
        CompleteImmortalGiftDeclaration(game, guard, summon: true);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.DoesNotContain(drawn, player.Hand);
        Assert.Contains(guard, player.Graveyard);
        var result = Assert.Single(ResultsFor(game, source.InstanceId));
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal("negated", result.EffectResultStatus);
    }

    [Fact]
    public void ImmortalGiftDrawFailureStopsItsSubsequentEntrySegment()
    {
        var game = Create(308006);
        var player = game.State.Players[0];
        var source = Card("S01-0223", "gift-draw-failed");
        var guard = Card("S01-0212", "gift-draw-failed-guard");
        source.Hidden = true;
        player.Field[1][0] = source;
        player.Graveyard.Add(guard);

        QueueReaction(game, 0, source);
        CompleteImmortalGiftDeclaration(game, guard, summon: true);
        PassResponses(game);

        Assert.Contains(guard, player.Graveyard);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == guard.InstanceId);
        var result = Assert.Single(ResultsFor(game, source.InstanceId));
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal("failed", result.EffectResultStatus);
    }

    [Fact]
    public void SeppukuRestoreKeepsTargetAndSkipsOnlyItsInvalidSecondSegment()
    {
        var game = Create(308004, stateFormatVersion: 2);
        var source = Card("S01-0420", "seppuku-restore", owner: 0);
        var target = Card("S01-0101", "seppuku-restore-target", owner: 1);
        var drawn = Card("S01-0401", "seppuku-restore-draw", owner: 0);
        source.Hidden = true;
        game.State.Players[0].Field[1][0] = source;
        game.State.Players[0].Library.Add(drawn);
        game.State.Players[1].Field[0][0] = target;

        QueueReaction(game, 0, source);
        Resolve(game, target.InstanceId);
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("trigger:S01-0420:reaction", first.Data["compositePlan"]);
        Assert.Equal(target.InstanceId, first.Data["declared:costTarget"]);
        var sceneId = first.Data["presentationSceneId"];

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restored = Assert.Single(game.State.EffectStack);
        Assert.Equal(target.InstanceId, restored.Data["declared:costTarget"]);
        Assert.Equal(sceneId, restored.Data["presentationSceneId"]);
        var restoredTarget = Assert.IsType<L12CardInstance>(game.State.Players[1].Field[0][0]);
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Hand.Add(restoredTarget);

        PassResponses(game);

        Assert.Contains(drawn.InstanceId, game.State.Players[0].Hand.Select(card => card.InstanceId));
        Assert.Equal(0, restoredTarget.CostModifier);
        var results = ResultsFor(game, source.InstanceId).OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.True(results.Length == 2, LifecycleDiagnostic(game, source.InstanceId));
        Assert.Equal(["resolved", "failed"], results.Select(entry => entry.EffectResultStatus));
        Assert.Equal([1, 2], results.Select(entry => entry.EffectSegmentIndex));
    }

    [Fact]
    public void MandatorySeppukuWithNoLegalTargetStillDrawsThenSkipsTargetSegment()
    {
        var game = Create(308005);
        var source = Card("S01-0420", "seppuku-no-target");
        var drawn = Card("S01-0401", "seppuku-no-target-draw");
        source.Hidden = true;
        game.State.Players[0].Field[1][0] = source;
        game.State.Players[0].Library.Add(drawn);

        QueueReaction(game, 0, source);

        Assert.Empty(game.State.PendingActivations);
        var stack = Assert.Single(game.State.EffectStack);
        Assert.Equal("seppuku-draw", stack.Data["atomicFlow"]);
        PassResponses(game);

        Assert.Contains(drawn, game.State.Players[0].Hand);
        var results = ResultsFor(game, source.InstanceId).OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.True(results.Length == 2, LifecycleDiagnostic(game, source.InstanceId));
        Assert.Equal(["resolved", "skipped"], results.Select(entry => entry.EffectResultStatus));
    }

    [Fact]
    public void SeppukuDrawFailureStopsItsSubsequentCostSegment()
    {
        var game = Create(308007);
        var source = Card("S01-0420", "seppuku-draw-failed");
        source.Hidden = true;
        game.State.Players[0].Field[1][0] = source;

        QueueReaction(game, 0, source);
        PassResponses(game);

        var result = Assert.Single(ResultsFor(game, source.InstanceId));
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal("failed", result.EffectResultStatus);
    }

    [Fact]
    public void LegacyImmortalGiftStackStillCompletesBothClausesAfterUpgrade()
    {
        var game = Create(308008);
        var player = game.State.Players[0];
        var source = Card("S01-0223", "gift-legacy-source");
        var guard = Card("S01-0212", "gift-legacy-guard");
        var drawn = Card("S01-0201", "gift-legacy-draw");
        player.Resolving.Add(source);
        player.Graveyard.Add(guard);
        player.Library.Add(drawn);
        var item = new L12StackItem
        {
            StackItemId = "gift-legacy-stack",
            Controller = 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            SourceSnapshot = source.Clone(),
            Trigger = "reaction",
            Text = "旧存档中的不朽之礼",
        };
        item.Data["declared:entryCard"] = guard.InstanceId;
        item.Data["declared:entrySlot"] = "0:0";
        game.State.EffectStack.Add(item);

        Invoke(game, "ResolveTopStack");

        Assert.Contains(drawn, player.Hand);
        Assert.Equal(guard.InstanceId, player.Field[0][0]?.InstanceId);
        Assert.False(item.Data.ContainsKey("compositePlan"));
    }

    private static string LifecycleDiagnostic(L12GameEngine game, string sourceInstanceId)
        => $"stack=[{string.Join(',', game.State.EffectStack.Select(item => item.Data.GetValueOrDefault("atomicFlow")))}] "
            + $"deferred=[{string.Join(',', game.State.DeferredEffectStack.Select(item => item.Data.GetValueOrDefault("atomicFlow")))}] "
            + $"prompts=[{string.Join(',', game.State.PendingPrompts.Select(prompt => $"{prompt.Kind}/{prompt.Continuation}"))}] "
            + $"events=[{string.Join(';', game.State.Events.Where(entry => entry.Cards.Any(card => card.InstanceId == sourceInstanceId))
                .Select(entry => $"{entry.Type}:{entry.EffectResultStatus}:{entry.EffectSegmentIndex}:{entry.EffectText}"))}]";
}
