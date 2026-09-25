using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6ERegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6e", "ATOMIC6E", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.Removed.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.Morale.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? owner = 0, int? cost = null)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12Prompt ResolveMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static void InvokePrivateZoneSummon(L12GameEngine game, L12PlayerState player,
        string instanceId, string slot, bool tapped)
    {
        var method = typeof(L12GameEngine).GetMethod("SummonFromAnyPrivateZone",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [player, instanceId, slot, tapped]);
    }

    private static int CountInstance(L12GameEngine game, string instanceId)
        => game.State.Players.Sum(player =>
            player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId));

    public static IEnumerable<object[]> ReviewedSummons()
    {
        yield return ["S01-0208", "S01-0212", false, true];
        yield return ["S01-0210", "S01-0201", false, false];
        yield return ["S01-0305", "S01-0305", false, true];
        yield return ["S01-0308", "S01-0301", false, false];
        yield return ["S01-0309", "S01-0310", true, false];
        yield return ["S02-0202", "S01-0212", false, false];
        yield return ["S02-0203", "S02-0201", false, false];
        yield return ["S02-0205", "S02-0201", false, false];
        yield return ["S02-0601", "S02-0606", true, false];
    }

    public static IEnumerable<object[]> DeathGraveSummonRows()
    {
        yield return ["S01-0210", "S01-0212"];
        yield return ["S01-0308", "S01-0301"];
        yield return ["S02-0202", "S01-0212"];
    }

    [Theory]
    [MemberData(nameof(DeathGraveSummonRows))]
    [Trait("L12Evidence", "entry:death-grave-summon-shared-lifecycle")]
    public void MandatoryDeathGraveSummonsRequireCardAndSlotEvenWhenTargetIsUnique(
        string sourceCardId, string targetCardId)
    {
        var game = Create(7890 + sourceCardId[^1]);
        var player = game.State.Players[0];
        var source = Card(sourceCardId, $"death-summon-source-{sourceCardId}");
        var target = Card(targetCardId, $"death-summon-target-{sourceCardId}",
            cost: sourceCardId is "S01-0210" or "S01-0308" ? 2 : null);
        var invalid = Card("S01-0002", $"death-summon-invalid-{sourceCardId}");
        player.Field[0][0] = source;
        player.Graveyard.AddRange([target, invalid]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: source.InstanceId)).Accepted);
        var cardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("grave-card", cardPrompt.Kind);
        Assert.Equal([target.InstanceId], cardPrompt.ValidChoices);
        Assert.DoesNotContain("skip", cardPrompt.ValidChoices);
        var displayed = cardPrompt.Data["displayCardIds"].Split('|');
        Assert.Contains(target.InstanceId, displayed);
        Assert.Contains(invalid.InstanceId, displayed);
        ResolveMany(game, target.InstanceId);

        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        Assert.Contains("0:1", slotPrompt.ValidChoices);
        Assert.DoesNotContain("skip", slotPrompt.ValidChoices);
        Resolve(game, "0:1");
        var stack = Assert.Single(game.State.EffectStack);
        Assert.Equal(target.InstanceId, stack.Data.GetValueOrDefault("declared:entryCard"));
        Assert.Equal("0:1", stack.Data.GetValueOrDefault("declared:entrySlot"));
        PassResponses(game);

        Assert.Same(target, player.Field[0][1]);
        Assert.False(target.Tapped);
        Assert.DoesNotContain(target, player.Graveyard);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == sourceCardId));
    }

    [Theory]
    [MemberData(nameof(DeathGraveSummonRows))]
    [Trait("L12Evidence", "entry:death-grave-summon-slot-revalidate")]
    public void MandatoryDeathGraveSummonsFailIfDeclaredSlotIsOccupiedDuringResponses(
        string sourceCardId, string targetCardId)
    {
        var game = Create(7895 + sourceCardId[^1]);
        var player = game.State.Players[0];
        var source = Card(sourceCardId, $"death-summon-late-source-{sourceCardId}");
        var target = Card(targetCardId, $"death-summon-late-target-{sourceCardId}",
            cost: sourceCardId is "S01-0210" or "S01-0308" ? 2 : null);
        player.Field[0][0] = source;
        player.Graveyard.Add(target);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: source.InstanceId)).Accepted);
        ResolveMany(game, target.InstanceId);
        Resolve(game, "0:0");
        var blocker = Card("S01-0003", $"death-summon-late-blocker-{sourceCardId}");
        player.Field[0][0] = blocker;
        PassResponses(game);

        Assert.Same(blocker, player.Field[0][0]);
        Assert.Contains(target, player.Graveyard);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.CardId == sourceCardId));
    }

    [Theory]
    [MemberData(nameof(DeathGraveSummonRows))]
    [Trait("L12Evidence", "entry:death-grave-summon-no-target")]
    public void MandatoryDeathGraveSummonsSilentlySkipWhenNoTargetExists(
        string sourceCardId, string targetCardId)
    {
        _ = targetCardId;
        var game = Create(7898 + sourceCardId[^1]);
        var source = Card(sourceCardId, $"death-summon-empty-source-{sourceCardId}");
        game.State.Players[0].Field[0][0] = source;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: source.InstanceId)).Accepted);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
    }

    [Theory]
    [MemberData(nameof(ReviewedSummons))]
    [Trait("L12Evidence", "entry:resolution-slot-revalidation")]
    public void ReviewedSummonsNeverOverwriteAnOccupiedDeclaredSlotAndKeepTheRealCardInItsSourceZone(
        string sourceCardId, string summonedCardId, bool fromHand, bool tapped)
    {
        var game = Create(7900 + sourceCardId.GetHashCode(StringComparison.Ordinal));
        var player = game.State.Players[0];
        var summoned = Card(summonedCardId, $"batch6e-target-{sourceCardId}");
        var blocker = Card("S01-0003", $"batch6e-blocker-{sourceCardId}");
        if (fromHand) player.Hand.Add(summoned);
        else player.Graveyard.Add(summoned);
        player.Field[0][1] = blocker;

        InvokePrivateZoneSummon(game, player, summoned.InstanceId, "0:1", tapped);

        Assert.Same(blocker, player.Field[0][1]);
        Assert.Contains(summoned, fromHand ? player.Hand : player.Graveyard);
        Assert.Equal(1, CountInstance(game, summoned.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("位置", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:same-time-slot-collision")]
    public void TwoDeclaredSummonsForTheSameSlotDoNotOverwriteTheFirstResolvedCard()
    {
        var game = Create(7910);
        var player = game.State.Players[0];
        var first = Card("S01-0212", "batch6e-collision-first");
        var second = Card("S02-0201", "batch6e-collision-second");
        player.Graveyard.AddRange([first, second]);

        InvokePrivateZoneSummon(game, player, first.InstanceId, "0:0", tapped: false);
        InvokePrivateZoneSummon(game, player, second.InstanceId, "0:0", tapped: false);

        Assert.Same(first, player.Field[0][0]);
        Assert.Contains(second, player.Graveyard);
        Assert.Equal(1, CountInstance(game, first.InstanceId));
        Assert.Equal(1, CountInstance(game, second.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0305")]
    [Trait("L12Evidence", "entry:death-colon-cost-pre-stack")]
    public void BjornPaysBothColonCostsBeforeItsRevivalEffectEntersTheStack()
    {
        var game = Create(7911);
        var player = game.State.Players[0];
        var bjorn = Card("S01-0305", "batch6e-bjorn-prepay");
        var costs = Enumerable.Range(1, 4)
            .Select(index => Card($"S01-000{index}", $"batch6e-bjorn-cost-{index}"))
            .ToArray();
        player.Field[0][0] = bjorn;
        player.Graveyard.AddRange(costs);
        var hpBefore = player.Hp;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: bjorn.InstanceId)).Accepted);
        ResolveMany(game, costs.Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:1");

        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.DoesNotContain(costs, card => player.Graveyard.Contains(card));
        Assert.Equal(costs, player.Library);
        Assert.Contains(bjorn, player.Graveyard);
        Assert.Single(game.State.EffectStack, item => item.SourceCardId == "S01-0305");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0305")]
    [Trait("L12Evidence", "entry:death-colon-cost-revival-resolution")]
    public void BjornRevivesRestedAfterItsPrepaidCostsResolve()
    {
        var game = Create(79111);
        var player = game.State.Players[0];
        var bjorn = Card("S01-0305", "batch6e-bjorn-resolve");
        var costs = Enumerable.Range(1, 4)
            .Select(index => Card($"S01-000{index}", $"batch6e-bjorn-resolve-cost-{index}"))
            .ToArray();
        player.Field[0][0] = bjorn;
        player.Graveyard.AddRange(costs);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: bjorn.InstanceId)).Accepted);
        ResolveMany(game, costs.Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:1");
        PassResponses(game);

        Assert.Same(bjorn, player.Field[0][1]);
        Assert.True(bjorn.Tapped);
        Assert.DoesNotContain(bjorn, player.Graveyard);
        Assert.Equal(1, CountInstance(game, bjorn.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == bjorn.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0305")]
    [Trait("L12Evidence", "entry:weighted-grave-cost-revival-resolution")]
    public void BjornWeightedGraveCostKeepsPhysicalOrderAndStillRevives()
    {
        var game = Create(79112);
        var player = game.State.Players[0];
        var bjorn = Card("S01-0305", "batch6e-bjorn-weighted");
        var warrior = Card("ST03-08", "batch6e-bjorn-weighted-warrior");
        var ordinary = Card("S01-0001", "batch6e-bjorn-weighted-ordinary");
        player.Field[0][0] = bjorn;
        player.Graveyard.AddRange([warrior, ordinary]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: bjorn.InstanceId)).Accepted);
        var graveChoice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(2, graveChoice.MinChoose);
        Assert.Equal(2, graveChoice.MaxChoose);
        ResolveMany(game, warrior.InstanceId, ordinary.InstanceId);

        var representedCount = Assert.Single(game.State.PendingPrompts);
        var asThree = Assert.Single(representedCount.ValidChoices,
            choice => representedCount.ChoiceLabels[choice].Contains("视为3张", StringComparison.Ordinal));
        Resolve(game, asThree);
        Resolve(game, "0:1");
        PassResponses(game);

        Assert.Equal([warrior.InstanceId, ordinary.InstanceId],
            player.Library.Select(card => card.InstanceId));
        Assert.Same(bjorn, player.Field[0][1]);
        Assert.True(bjorn.Tapped);
        Assert.DoesNotContain(bjorn, player.Graveyard);
        Assert.Equal(1, CountInstance(game, bjorn.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0305")]
    [Trait("L12Evidence", "entry:death-slot-invalid-no-cost-refund")]
    public void BjornSlotInvalidationDoesNotOverwriteOrRefundItsPrepaidCosts()
    {
        var game = Create(7912);
        var player = game.State.Players[0];
        var bjorn = Card("S01-0305", "batch6e-bjorn-invalid-slot");
        var costs = Enumerable.Range(1, 4)
            .Select(index => Card($"S01-000{index}", $"batch6e-bjorn-paid-{index}"))
            .ToArray();
        player.Field[0][0] = bjorn;
        player.Graveyard.AddRange(costs);
        var hpBefore = player.Hp;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: bjorn.InstanceId)).Accepted);
        ResolveMany(game, costs.Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:1");
        var blocker = Card("S01-0003", "batch6e-bjorn-blocker");
        player.Field[0][1] = blocker;
        PassResponses(game);

        Assert.Same(blocker, player.Field[0][1]);
        Assert.Contains(bjorn, player.Graveyard);
        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.Equal(costs, player.Library);
        Assert.True(game.State.Events.Any(entry => entry.Type == "effect-failed"
                && entry.Text.Contains("位置不再为空", StringComparison.Ordinal)
                && entry.Text.Contains("费用不返还", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, game.State.Events.Select(entry => $"{entry.Type}: {entry.Text}")));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0205")]
    [Trait("L12Evidence", "entry:active-rest-slot-invalid-keeps-rest-cost-without-once")]
    public void GoldenScarabActiveSlotInvalidationKeepsItsRestCostWithoutInventingOncePerTurn()
    {
        var game = Create(7913);
        var player = game.State.Players[0];
        var source = Card("S02-0205", "batch6e-golden-active");
        var summoned = Card("S02-0201", "batch6e-golden-active-target");
        player.Relic = source;
        player.Graveyard.Add(summoned);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "scarabSummon")).Accepted);
        Resolve(game, summoned.InstanceId);
        Resolve(game, "0:1");
        Assert.True(source.Tapped);
        var blocker = Card("S01-0003", "batch6e-golden-active-blocker");
        player.Field[0][1] = blocker;
        PassResponses(game);

        Assert.Same(blocker, player.Field[0][1]);
        Assert.Contains(summoned, player.Graveyard);
        Assert.True(source.Tapped);
        Assert.DoesNotContain($"active:{source.InstanceId}:scarabSummon", player.UsedAbilities);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("位置", StringComparison.Ordinal));
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0205")]
    public void GoldenScarabNegationKeepsItsRestCostAndPublishesNegatedResult()
    {
        var game = Create(79131);
        var player = game.State.Players[0];
        var source = Card("S02-0205", "batch6e-golden-negated");
        var summoned = Card("S02-0201", "batch6e-golden-negated-target");
        player.Relic = source;
        player.Graveyard.Add(summoned);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "scarabSummon")).Accepted);
        Resolve(game, summoned.InstanceId);
        Resolve(game, "0:1");
        var stackItem = Assert.Single(game.State.EffectStack);
        stackItem.Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Contains(summoned, player.Graveyard);
        Assert.Null(player.Field[0][1]);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal("negated", result.EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0204")]
    [Trait("L12Evidence", "entry:batch6d-owner-slot-control")]
    public void EmptyDeclaredSlotStillAcceptsTheRealGuardWithoutDuplicatingIt()
    {
        var game = Create(7914);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "batch6e-success-control");
        player.Graveyard.Add(guard);

        InvokePrivateZoneSummon(game, player, guard.InstanceId, "1:2", tapped: true);

        Assert.Same(guard, player.Field[1][2]);
        Assert.True(guard.Tapped);
        Assert.DoesNotContain(guard, player.Graveyard);
        Assert.Equal(1, CountInstance(game, guard.InstanceId));
    }
}
