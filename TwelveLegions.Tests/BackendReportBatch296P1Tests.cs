using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackendReportBatch296P1Tests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "backend-report-batch296-p1", "BATCH296P1", seed,
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static L12MoraleCard Morale(string instanceId, bool tapped = false,
        bool godPower = false, int cannotUntapUntilRound = 0)
        => new()
        {
            CardId = godPower ? "S02-05C1" : "S01-04C1",
            InstanceId = instanceId,
            Tapped = tapped,
            IsGodPower = godPower,
            CannotUntapUntilRound = cannotUntapUntilRound,
        };

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static (L12CardInstance Source, L12MoraleCard PaidMorale) ArrangePaidOiran(
        L12GameEngine game, string suffix)
    {
        var player = game.State.Players[0];
        var source = Card("S01-0419", $"batch296-oiran-{suffix}");
        var paidMorale = Morale($"batch296-paid-{suffix}");
        player.Hand.Add(source);
        player.Morale.Add(paidMorale);
        player.Library.AddRange([
            Card("S01-0401", $"batch296-library-{suffix}-1"),
            Card("S01-0101", $"batch296-library-{suffix}-2"),
            Card("S01-0201", $"batch296-library-{suffix}-3"),
            Card("S01-0301", $"batch296-library-{suffix}-4"),
        ]);
        return (source, paidMorale);
    }

    private static L12Prompt NegateSearchAndReachDeferredMode(L12GameEngine game)
    {
        var search = Assert.Single(game.State.EffectStack);
        Assert.Equal("oiran-search", search.Data.GetValueOrDefault("atomicFlow"));
        search.Negated = true;
        PassResponses(game);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:morale", mode.ValidChoices);
        return mode;
    }

    private static L12Prompt ResolveSearchAndReachDeferredMode(L12GameEngine game,
        L12CardInstance chosen, params L12CardInstance[] bottom)
    {
        PassResponses(game);
        var pick = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("oiran-pick", pick.Data.GetValueOrDefault("action"));
        var pickResult = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: pick.PromptId, Choice: chosen.InstanceId));
        Assert.True(pickResult.Accepted, pickResult.Error);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("oiran-order", order.Data.GetValueOrDefault("action"));
        var orderResult = game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: bottom.Select(card => card.InstanceId).ToList()));
        Assert.True(orderResult.Accepted, orderResult.Error);
        PassResponses(game);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:morale", mode.ValidChoices);
        return mode;
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-f93e480c")]
    [Trait("L12Evidence", "card:S01-0419")]
    public void PaidMoraleBecomesADeferredSecondSegmentTargetWithItsOwnResponseWindow()
    {
        var game = Create(29601);
        var player = game.State.Players[0];
        var (source, paidMorale) = ArrangePaidOiran(game, "paid-target");

        var play = game.Handle(0, new L12Command("playCard", source.InstanceId));

        Assert.True(play.Accepted, play.Error);
        Assert.DoesNotContain(source, player.Hand);
        Assert.Contains(source, player.Resolving);
        Assert.True(paidMorale.Tapped);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("oiran-search", first.Data.GetValueOrDefault("atomicFlow"));
        first.Negated = true;

        var mode = NegateSearchAndReachDeferredMode(game);
        Resolve(game, "mode:morale");
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(paidMorale.InstanceId, target.ValidChoices);
        Resolve(game, paidMorale.InstanceId);

        var second = Assert.Single(game.State.EffectStack);
        Assert.Equal("oiran-ready-morale", second.Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal("1", second.Data.GetValueOrDefault("compositeSegment"));
        Assert.Equal(paidMorale.InstanceId, second.Data.GetValueOrDefault("declared:moraleTarget"));
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        Assert.NotEqual(mode.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
        PassResponses(game);

        Assert.False(paidMorale.Tapped);
        Assert.Contains(source, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-f93e480c")]
    [Trait("L12Evidence", "entry:decline-without-refund")]
    public void DecliningDeferredReadyDoesNotRefundOrChargeTheAlreadyPaidCardAgain()
    {
        var game = Create(29602);
        var player = game.State.Players[0];
        var (source, paidMorale) = ArrangePaidOiran(game, "decline");

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        NegateSearchAndReachDeferredMode(game);
        Resolve(game, "mode:none");

        Assert.True(paidMorale.Tapped);
        Assert.Single(player.Morale);
        Assert.Contains(source, player.Graveyard);
        Assert.DoesNotContain(source, player.Hand);
        Assert.DoesNotContain(source, player.Resolving);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-f93e480c")]
    [Trait("L12Evidence", "entry:late-target-invalidity-is-segment-local")]
    public void LateTargetInvalidityKeepsTheResolvedSearchAndCancelsOnlyReadySegment()
    {
        var game = Create(29603);
        var player = game.State.Players[0];
        var (source, paidMorale) = ArrangePaidOiran(game, "late-invalid");
        var chosen = player.Library[0];
        var bottom = player.Library.Skip(1).Take(2).ToArray();

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        ResolveSearchAndReachDeferredMode(game, chosen, bottom);
        Resolve(game, "mode:morale");
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(paidMorale.InstanceId, target.ValidChoices);

        paidMorale.Tapped = false;
        var invalid = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: target.PromptId, Choice: paidMorale.InstanceId));

        Assert.True(invalid.Accepted, invalid.Error);
        Assert.Contains(chosen, player.Hand);
        Assert.False(paidMorale.Tapped);
        Assert.Contains(source, player.Graveyard);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingActivations);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("目标", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-f93e480c")]
    [Trait("L12Evidence", "entry:checkpoint-and-stale-submit")]
    public void DeferredDeclarationSurvivesCheckpointAndRejectsARepeatedOldPrompt()
    {
        var game = Create(29604);
        var (_, paidMorale) = ArrangePaidOiran(game, "restore");
        Assert.True(game.Handle(0,
            new L12Command("playCard", "batch296-oiran-restore")).Accepted);
        var oldMode = NegateSearchAndReachDeferredMode(game);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredMode = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal(oldMode.PromptId, restoredMode.PromptId);
        Assert.True(restored.Handle(0, new L12Command("resolvePrompt", PromptId: restoredMode.PromptId,
            Choice: "mode:morale")).Accepted);

        var stale = restored.Handle(0, new L12Command("resolvePrompt", PromptId: restoredMode.PromptId,
            Choice: "mode:morale"));
        Assert.False(stale.Accepted);
        Assert.Single(restored.State.PendingActivations);
        var target = Assert.Single(restored.State.PendingPrompts);
        Assert.Contains(paidMorale.InstanceId, target.ValidChoices);
        Assert.True(restored.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: paidMorale.InstanceId)).Accepted);

        Assert.Single(restored.State.EffectStack, item =>
            item.Data.GetValueOrDefault("atomicFlow") == "oiran-ready-morale");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0419")]
    [Trait("L12Evidence", "entry:non-equivalent-current-morale-targets")]
    public void DeferredDeclarationEnumeratesEveryCurrentRestedMoraleIdentity()
    {
        var game = Create(29605);
        var player = game.State.Players[0];
        player.FreeTacticCount = 1;
        var source = Card("S01-0419", "batch296-oiran-non-equivalent");
        var ordinary = Morale("batch296-rested-ordinary", tapped: true);
        var godPower = Morale("batch296-rested-god-power", tapped: true, godPower: true);
        var locked = Morale("batch296-rested-locked", tapped: true,
            cannotUntapUntilRound: game.State.Round + 1);
        player.Hand.Add(source);
        player.Morale.AddRange([ordinary, godPower, locked]);
        player.Library.AddRange([
            Card("S01-0401", "batch296-non-equivalent-library-1"),
            Card("S01-0101", "batch296-non-equivalent-library-2"),
            Card("S01-0201", "batch296-non-equivalent-library-3"),
        ]);

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        NegateSearchAndReachDeferredMode(game);
        Resolve(game, "mode:morale");

        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(ordinary.InstanceId, target.ValidChoices);
        Assert.Contains(godPower.InstanceId, target.ValidChoices);
        Assert.Contains(locked.InstanceId, target.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0419")]
    [Trait("L12Evidence", "entry:second-segment-negation-is-local")]
    public void NegatingDeferredReadyKeepsTheCompletedSearchAndPaidMoraleRested()
    {
        var game = Create(29606);
        var player = game.State.Players[0];
        var (source, paidMorale) = ArrangePaidOiran(game, "second-negated");
        var chosen = player.Library[0];
        var bottom = player.Library.Skip(1).Take(2).ToArray();

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        ResolveSearchAndReachDeferredMode(game, chosen, bottom);
        Resolve(game, "mode:morale");
        Resolve(game, paidMorale.InstanceId);
        var ready = Assert.Single(game.State.EffectStack);
        Assert.Equal("oiran-ready-morale", ready.Data.GetValueOrDefault("atomicFlow"));
        ready.Negated = true;
        PassResponses(game);

        Assert.Contains(chosen, player.Hand);
        Assert.True(paidMorale.Tapped);
        Assert.Contains(source, player.Graveyard);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0419")]
    [Trait("L12Evidence", "entry:target-step-cancel-is-segment-local")]
    public void CancellingDeferredTargetDoesNotUndoThePaidCardOrStrandResolvingState()
    {
        var game = Create(29607);
        var player = game.State.Players[0];
        var (source, paidMorale) = ArrangePaidOiran(game, "target-cancel");

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        NegateSearchAndReachDeferredMode(game);
        Resolve(game, "mode:morale");
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", target.ValidChoices);
        Resolve(game, "skip");

        Assert.True(paidMorale.Tapped);
        Assert.Contains(source, player.Graveyard);
        Assert.DoesNotContain(source, player.Resolving);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("效果段与费用均不回退", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0419")]
    [Trait("L12Evidence", "entry:no-current-target-skips-without-stranding")]
    public void FreePlayWithoutAnyRestedMoraleSkipsDeferredSegmentAndLeavesResolving()
    {
        var game = Create(29608);
        var player = game.State.Players[0];
        player.FreeTacticCount = 1;
        var source = Card("S01-0419", "batch296-oiran-no-target");
        player.Hand.Add(source);
        player.Library.AddRange([
            Card("S01-0401", "batch296-no-target-library-1"),
            Card("S01-0101", "batch296-no-target-library-2"),
            Card("S01-0201", "batch296-no-target-library-3"),
        ]);

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        var search = Assert.Single(game.State.EffectStack);
        search.Negated = true;
        PassResponses(game);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(source, player.Resolving);
        Assert.Contains(source, player.Graveyard);
    }
}
