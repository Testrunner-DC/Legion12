using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AyMandatoryEnterRegressionTests
{
    private const string AyCardId = "S01-0208";
    private const string TombGuardCardId = "S01-0212";
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:mandatory-enter-declaration")]
    public void AyMandatoryEntryRejectsSkipAndSummonsExactlyOnceForEitherController(
        int controller, bool restoreBeforeSlot)
    {
        var game = Create(2026100901 + controller);
        var (ayId, guardId) = BeginAyEntry(game, controller);
        var activation = Assert.Single(game.State.PendingActivations);
        Assert.Equal(["grave-card", "effect-entry-battlefield", "effect-entry-slot"],
            activation.SelectionSteps.Select(step => step.Kind).ToArray());
        Assert.All(activation.SelectionSteps,
            step => Assert.Equal(L12ActivationCancellationPolicy.NotAllowed, step.CancellationPolicy));

        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("grave-card", guardPrompt.Kind);
        Assert.DoesNotContain("skip", guardPrompt.ValidChoices);
        RejectWithoutMutation(game, controller, guardPrompt, guardId);
        Resolve(game, controller, guardPrompt, guardId);

        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        Assert.DoesNotContain("skip", slotPrompt.ValidChoices);
        Assert.Equal($"battlefield:{controller}",
            Assert.Single(Assert.Single(game.State.PendingActivations)
                .DeclaredValues["entryBattlefield"]));
        RejectWithoutMutation(game, controller, slotPrompt, guardId);

        if (restoreBeforeSlot)
        {
            game = Restore(game);
            slotPrompt = Assert.Single(game.State.PendingPrompts);
            Assert.All(Assert.Single(game.State.PendingActivations).SelectionSteps,
                step => Assert.Equal(L12ActivationCancellationPolicy.NotAllowed, step.CancellationPolicy));
        }

        var slot = slotPrompt.ValidChoices.First(choice => choice != "skip");
        var commit = new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId, Choice: slot);
        Assert.True(game.Handle(controller, commit).Accepted);
        Assert.False(game.Handle(controller, commit).Accepted);
        PassResponses(game);

        var player = game.State.Players[controller];
        var placed = player.Field.SelectMany(row => row).Where(card => card?.InstanceId == guardId).ToArray();
        Assert.Single(placed);
        Assert.True(placed[0]!.Tapped);
        Assert.Equal(1, CountAuthoritativeInstances(game, guardId));
        Assert.Equal(1, CountAuthoritativeInstances(game, ayId));
        AssertIdle(game);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:mandatory-enter-no-object")]
    public void AyMandatoryEntryWithNoGuardOrNoSlotSettlesWithoutADeclaration(bool fillAllSlots)
    {
        var game = Create(fillAllSlots ? 2026100903 : 2026100904);
        var player = PrepareTurn(game, 0);
        var ay = TakeCard(player, AyCardId);
        var guards = player.Graveyard.Where(card => card.CardId == TombGuardCardId).ToArray();
        if (fillAllSlots)
        {
            for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (row != 0 || slot != 0)
                    player.Field[row][slot] = Card("S01-0002", $"ay-full-{row}-{slot}", 0);
        }
        else
        {
            foreach (var guard in guards)
            {
                player.Graveyard.Remove(guard);
                player.Removed.Add(guard);
            }
        }

        Assert.True(game.Handle(0, new L12Command("playCard", ay.InstanceId, Row: 0, Slot: 0)).Accepted);
        DrainResponsesAndMandatoryEffects(game);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(guards.Length, guards.Sum(guard => CountAuthoritativeInstances(game, guard.InstanceId)));
        if (fillAllSlots)
            Assert.All(guards, guard => Assert.Contains(player.Graveyard, card => card.InstanceId == guard.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:mandatory-enter-target-invalidated")]
    public void AyDoesNotSubstituteOrDuplicateADeclaredGuardThatLeavesBeforeCommit()
    {
        var game = Create(2026100905);
        var (_, guardId) = BeginAyEntry(game, 0);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Resolve(game, 0, guardPrompt, guardId);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        var slot = slotPrompt.ValidChoices.First(choice => choice != "skip");
        var player = game.State.Players[0];
        var guard = Assert.Single(player.Graveyard, card => card.InstanceId == guardId);
        player.Graveyard.Remove(guard);
        player.Removed.Add(guard);

        Assert.True(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId, Choice: slot)).Accepted);
        PassResponses(game);

        Assert.Contains(player.Removed, card => card.InstanceId == guardId);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.CardId == TombGuardCardId);
        Assert.Equal(1, CountAuthoritativeInstances(game, guardId));
        AssertIdle(game);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:mandatory-enter-source-snapshot")]
    public void AySourceLeavingAfterItsTriggerDoesNotCancelTheMandatoryGuardEntry()
    {
        var game = Create(2026100906);
        var (ayId, guardId) = BeginAyEntry(game, 0);
        var player = game.State.Players[0];
        var sourceSlot = FindFieldSlot(player, ayId);
        var ay = Assert.IsType<L12CardInstance>(player.Field[sourceSlot.Row][sourceSlot.Slot]);
        player.Field[sourceSlot.Row][sourceSlot.Slot] = null;
        player.Graveyard.Add(ay);

        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Resolve(game, 0, guardPrompt, guardId);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Resolve(game, 0, slotPrompt, slotPrompt.ValidChoices.First(choice => choice != "skip"));
        PassResponses(game);

        Assert.Contains(player.Graveyard, card => card.InstanceId == ayId);
        Assert.Single(player.Field.SelectMany(row => row), card => card?.InstanceId == guardId);
        Assert.Equal(1, CountAuthoritativeInstances(game, ayId));
        Assert.Equal(1, CountAuthoritativeInstances(game, guardId));
        AssertIdle(game);
    }

    private static L12GameEngine Create(int seed)
        => new(Catalog, "ay-mandatory-enter", "AY-MANDATORY", seed,
            ["甲", "乙"], [2, 2], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);

    private static L12PlayerState PrepareTurn(L12GameEngine game, int controller)
    {
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        var player = game.State.Players[controller];
        while (player.MoraleDeck.Count > 0)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            player.Morale.Add(morale);
        }
        return player;
    }

    private static (string AyId, string GuardId) BeginAyEntry(L12GameEngine game, int controller)
    {
        var player = PrepareTurn(game, controller);
        var ay = TakeCard(player, AyCardId);
        var guard = Assert.Single(player.Graveyard.Where(card => card.CardId == TombGuardCardId).Take(1));
        Assert.True(game.Handle(controller,
            new L12Command("playCard", ay.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        Assert.Equal("pending-activation", Assert.Single(game.State.PendingPrompts).Continuation);
        return (ay.InstanceId, guard.InstanceId);
    }

    private static L12CardInstance TakeCard(L12PlayerState player, string cardId)
    {
        var card = player.Hand.Concat(player.Library).FirstOrDefault(candidate => candidate.CardId == cardId)
            ?? Card(cardId, $"ay-test-{player.PlayerIndex}-{cardId}", player.PlayerIndex);
        player.Hand.Remove(card);
        player.Library.Remove(card);
        player.Hand.Add(card);
        return card;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits], Profession = definition.Profession, OwnerIndex = owner,
        };
    }

    private static void RejectWithoutMutation(L12GameEngine game, int controller, L12Prompt prompt, string guardId)
    {
        var before = game.SerializeFullState();
        var rejected = game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "skip"));
        Assert.False(rejected.Accepted);
        Assert.Equal(before, game.SerializeFullState());
        Assert.Equal(1, CountAuthoritativeInstances(game, guardId));
    }

    private static void Resolve(L12GameEngine game, int controller, L12Prompt prompt, string choice)
    {
        var result = game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            Resolve(game, prompt.PlayerIndex, prompt, "pass");
        }
    }

    private static void DrainResponsesAndMandatoryEffects(L12GameEngine game)
    {
        for (var safety = 0; safety < 100; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
            {
                PassResponses(game);
                continue;
            }
            if (game.State.PendingPrompts.Count == 0 && game.State.EffectStack.Count == 0
                && game.State.PendingTriggerStackCandidates.Count == 0)
                return;
            break;
        }
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static (int Row, int Slot) FindFieldSlot(L12PlayerState player, string instanceId)
    {
        for (var row = 0; row < player.Field.Length; row++)
        for (var slot = 0; slot < player.Field[row].Length; slot++)
            if (player.Field[row][slot]?.InstanceId == instanceId)
                return (row, slot);
        throw new Xunit.Sdk.XunitException($"Field card not found: {instanceId}");
    }

    private static int CountAuthoritativeInstances(L12GameEngine game, string instanceId)
        => game.State.Players.Sum(player =>
            player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId));

    private static void AssertIdle(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
    }
}
