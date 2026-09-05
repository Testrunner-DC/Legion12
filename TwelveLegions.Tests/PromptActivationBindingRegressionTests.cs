using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptActivationBindingRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 60447342)
        => new(Catalog, "prompt-binding-regression", "PROMPTBIND", seed,
            ["甲", "乙"], [2, 3], skipPreparation: true);

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
        };
    }

    private static (L12GameEngine Game, L12CardInstance Ankh, L12CardInstance Guard,
        L12CardInstance Discard, L12CardInstance Ay) PrepareAnkh()
    {
        var game = Create();
        var player = game.State.Players[0];
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        while (player.MoraleDeck.Count > 0)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            player.Morale.Add(morale);
        }
        var ankh = Card("S01-0215", "incident-ankh");
        var guard = Card("S01-0212", "incident-guard");
        var discard = Card("S01-0205", "incident-discard");
        var ay = Card("S01-0208", "incident-ay");
        guard.Tapped = true;
        player.Relic = ankh;
        player.Field[0][0] = guard;
        player.Hand.Add(discard);
        player.Hand.Add(ay);
        return (game, ankh, guard, discard, ay);
    }

    private static L12Command BoundChoice(L12Prompt prompt, string choice) => new(
        "resolvePrompt",
        PromptId: prompt.PromptId,
        Choice: choice,
        ActivationId: prompt.ActivationId,
        SourceInstanceId: prompt.SourceInstanceId,
        SourceCardId: prompt.SourceCardId,
        Step: prompt.Step,
        CreatedRevision: prompt.CreatedRevision,
        Controller: prompt.Controller);

    [Fact]
    public void PendingActivationPromptCarriesImmutableBindingThroughReconnectSnapshot()
    {
        var (game, ankh, _, _, _) = PrepareAnkh();

        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhReady")).Accepted);

        var activation = Assert.Single(game.State.PendingActivations);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.State.Revision > activation.CreatedRevision);
        Assert.Equal(activation.ActivationId, prompt.ActivationId);
        Assert.Equal(activation.SourceInstanceId, prompt.SourceInstanceId);
        Assert.Equal(activation.SourceCardId, prompt.SourceCardId);
        Assert.Equal(activation.CurrentStep, prompt.Step);
        Assert.Equal(activation.CreatedRevision, prompt.CreatedRevision);
        Assert.Equal(activation.Controller, prompt.Controller);

        var reconnectPrompt = Assert.Single(game.SnapshotFor(0).Prompts);
        var promptType = reconnectPrompt.GetType();
        Assert.Equal(prompt.ActivationId, promptType.GetProperty("ActivationId")!.GetValue(reconnectPrompt));
        Assert.Equal(prompt.SourceInstanceId, promptType.GetProperty("SourceInstanceId")!.GetValue(reconnectPrompt));
        Assert.Equal(prompt.SourceCardId, promptType.GetProperty("SourceCardId")!.GetValue(reconnectPrompt));
        Assert.Equal(prompt.Step, promptType.GetProperty("Step")!.GetValue(reconnectPrompt));
        Assert.Equal(prompt.CreatedRevision, promptType.GetProperty("CreatedRevision")!.GetValue(reconnectPrompt));
        Assert.Equal(prompt.Controller, promptType.GetProperty("Controller")!.GetValue(reconnectPrompt));
    }

    [Fact]
    public void AnkhPromptRejectsAyBindingAndBackgroundPlayWithoutMutatingEitherCard()
    {
        var (game, ankh, guard, _, ay) = PrepareAnkh();
        var player = game.State.Players[0];
        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhReady")).Accepted);
        var activation = Assert.Single(game.State.PendingActivations);
        var prompt = Assert.Single(game.State.PendingPrompts);

        var wrongRoute = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId,
            Choice: guard.InstanceId,
            ActivationId: activation.ActivationId,
            SourceInstanceId: ay.InstanceId,
            SourceCardId: ay.CardId,
            Step: activation.CurrentStep,
            CreatedRevision: activation.CreatedRevision,
            Controller: activation.Controller));
        Assert.False(wrongRoute.Accepted);
        Assert.Contains("绑定", wrongRoute.Error);
        Assert.Contains(prompt, game.State.PendingPrompts);
        Assert.Contains(activation, game.State.PendingActivations);
        Assert.False(ankh.Tapped);
        Assert.Contains(ay, player.Hand);

        var backgroundPlay = game.Handle(0, new L12Command("playCard", ay.InstanceId,
            Row: 0, Slot: 1, TargetPlayerIndex: 0));
        Assert.False(backgroundPlay.Accepted);
        Assert.Contains(ay, player.Hand);
        Assert.Null(player.Field[0][1]);
    }

    [Fact]
    public void LegacyClientMayResolveExactPromptButStaleStepRevisionAndDoubleSubmitAreRejected()
    {
        var (game, ankh, guard, discard, _) = PrepareAnkh();
        var player = game.State.Players[0];
        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhReady")).Accepted);
        var first = Assert.Single(game.State.PendingPrompts);

        // Compatibility: an older client may omit echoed binding fields, but the server still
        // validates the stored prompt against the one authoritative pending activation.
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId,
            Choice: guard.InstanceId)).Accepted);
        var second = Assert.Single(game.State.PendingPrompts);
        var activation = Assert.Single(game.State.PendingActivations);
        Assert.True(game.State.Revision > activation.CreatedRevision);
        Assert.Single(game.SnapshotFor(0).Prompts);
        Assert.Contains(activation, game.State.PendingActivations);

        var staleStep = game.Handle(0, new L12Command("resolvePrompt", PromptId: second.PromptId,
            Choice: discard.InstanceId,
            ActivationId: second.ActivationId,
            SourceInstanceId: second.SourceInstanceId,
            SourceCardId: second.SourceCardId,
            Step: second.Step - 1,
            CreatedRevision: second.CreatedRevision,
            Controller: second.Controller));
        Assert.False(staleStep.Accepted);
        Assert.Contains(second, game.State.PendingPrompts);
        Assert.Contains(discard, player.Hand);

        var staleRevision = game.Handle(0, new L12Command("resolvePrompt", PromptId: second.PromptId,
            Choice: discard.InstanceId,
            ActivationId: second.ActivationId,
            SourceInstanceId: second.SourceInstanceId,
            SourceCardId: second.SourceCardId,
            Step: second.Step,
            CreatedRevision: second.CreatedRevision - 1,
            Controller: second.Controller));
        Assert.False(staleRevision.Accepted);
        Assert.Contains(second, game.State.PendingPrompts);
        Assert.Contains(activation, game.State.PendingActivations);

        var submission = BoundChoice(second, discard.InstanceId);
        Assert.True(game.Handle(0, submission).Accepted);
        Assert.Contains(discard, player.Graveyard);
        Assert.DoesNotContain(discard, player.Hand);

        var duplicate = game.Handle(0, submission);
        Assert.False(duplicate.Accepted);
        Assert.Single(player.Graveyard, card => card.InstanceId == discard.InstanceId);
    }

    [Fact]
    public void CancellationIsAtomicAndARepeatedCancellationCannotSpendOrResumeTheActivation()
    {
        var (game, ankh, _, discard, _) = PrepareAnkh();
        var player = game.State.Players[0];
        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhReady")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        var cancellation = BoundChoice(prompt, "skip");

        Assert.True(game.Handle(0, cancellation).Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.False(ankh.Tapped);
        Assert.Contains(discard, player.Hand);

        Assert.False(game.Handle(0, cancellation).Accepted);
        Assert.False(ankh.Tapped);
        Assert.Contains(discard, player.Hand);
    }

    [Fact]
    public void SourceLeavingItsActiveZoneCancelsPromptTransactionAndNoLongerBlocksEndTurn()
    {
        var (game, ankh, _, discard, _) = PrepareAnkh();
        var player = game.State.Players[0];
        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhReady")).Accepted);
        player.Relic = null;
        player.Graveyard.Add(ankh);

        var endTurn = game.Handle(0, new L12Command("endTurn"));

        Assert.True(endTurn.Accepted, endTurn.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Equal(1, game.State.ActivePlayer);
        Assert.Contains(discard, player.Hand);
        Assert.False(ankh.Tapped);
    }

    [Fact]
    public void PromptOnlyAndActivationOnlyOrphansAreReconciledButValidTransactionStillBlocksEndTurn()
    {
        var (validGame, validAnkh, _, _, _) = PrepareAnkh();
        Assert.True(validGame.Handle(0, new L12Command("activateAbility", validAnkh.InstanceId,
            Ability: "ankhReady")).Accepted);
        var validEnd = validGame.Handle(0, new L12Command("endTurn"));
        Assert.False(validEnd.Accepted);
        Assert.Single(validGame.State.PendingPrompts);
        Assert.Single(validGame.State.PendingActivations);

        var (promptOnlyGame, promptOnlyAnkh, _, _, _) = PrepareAnkh();
        Assert.True(promptOnlyGame.Handle(0, new L12Command("activateAbility", promptOnlyAnkh.InstanceId,
            Ability: "ankhReady")).Accepted);
        promptOnlyGame.State.PendingActivations.Clear();
        var promptOnlyEnd = promptOnlyGame.Handle(0, new L12Command("endTurn"));
        Assert.True(promptOnlyEnd.Accepted, promptOnlyEnd.Error);
        Assert.Empty(promptOnlyGame.State.PendingPrompts);
        Assert.Equal(1, promptOnlyGame.State.ActivePlayer);

        var (activationOnlyGame, activationOnlyAnkh, _, _, _) = PrepareAnkh();
        Assert.True(activationOnlyGame.Handle(0, new L12Command("activateAbility", activationOnlyAnkh.InstanceId,
            Ability: "ankhReady")).Accepted);
        activationOnlyGame.State.PendingPrompts.Clear();
        var activationOnlyEnd = activationOnlyGame.Handle(0, new L12Command("endTurn"));
        Assert.True(activationOnlyEnd.Accepted, activationOnlyEnd.Error);
        Assert.Empty(activationOnlyGame.State.PendingActivations);
        Assert.Equal(1, activationOnlyGame.State.ActivePlayer);
    }
}
